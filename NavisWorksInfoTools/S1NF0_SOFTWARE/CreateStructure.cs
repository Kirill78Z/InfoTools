﻿using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;
using NavisWorksInfoTools.S1NF0_SOFTWARE.XML.St;
using NavisWorksInfoTools.S1NF0_SOFTWARE.XML.Cl;
using System.IO;
using static NavisWorksInfoTools.Constants;
using static Common.ExceptionHandling.ExeptionHandlingProcedures;
using System.Xml.Serialization;

namespace NavisWorksInfoTools.S1NF0_SOFTWARE
{
    [Plugin("CreateStructure",
            DEVELOPER_ID,
            ToolTip = "Создать структуру для проекта " + S1NF0_APP_NAME + " из сохраненных наборов выбора",
            DisplayName = S1NF0_APP_NAME + ". 3. Создание структуры из наборов выбора")]
    public class CreateStructure : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                Document doc = Application.ActiveDocument;
                DocumentSelectionSets selectionSets = doc.SelectionSets;
                FolderItem rootFolderItem = selectionSets.RootItem;
                List<FolderItem> folders = new List<FolderItem>();
                foreach (SavedItem item in rootFolderItem.Children)
                {
                    if (item is FolderItem)
                    {
                        folders.Add(item as FolderItem);
                    }
                }

                if (folders.Count == 0)
                {
                    WinForms.MessageBox.Show("Для работы этой команды необходимо наличие папок с сохраненными наборами выбора",
                        "Отменено", WinForms.MessageBoxButtons.OK);
                    return 0;
                }

                string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string initialFileName = "Проект";
                string docFileName = doc.FileName;
                if (!String.IsNullOrEmpty(docFileName))
                {
                    initialPath = Path.GetDirectoryName(docFileName);
                    initialFileName = Path.GetFileNameWithoutExtension(docFileName);
                }

                //Вывести окно для выбора корневой папки для формирования структуры
                SelectRootFolderWindow selectRootFolderWindow = new SelectRootFolderWindow(folders, true, initialPath);
                bool? result = selectRootFolderWindow.ShowDialog();
                if (result != null && result.Value)
                {
                    FolderItem rootFolder = selectRootFolderWindow.RootFolder;

                    //Запросить имя и путь создаваемого файла структуры
                    WinForms.SaveFileDialog saveFileDialog = new WinForms.SaveFileDialog();
                    saveFileDialog.InitialDirectory = initialPath;
                    saveFileDialog.Filter = "st.xml files (*.st.xml)|*.st.xml";
                    saveFileDialog.FilterIndex = 1;
                    saveFileDialog.RestoreDirectory = true;
                    if (!String.IsNullOrWhiteSpace(initialFileName))
                        saveFileDialog.FileName = initialFileName;
                    saveFileDialog.Title = "Укажите файл для создания структуры";

                    if (saveFileDialog.ShowDialog() == WinForms.DialogResult.OK)
                    {
                        string stFilename = saveFileDialog.FileName;
                        string name = Path.GetFileName(stFilename);
                        string clFilename = Path.Combine(Path.GetDirectoryName(stFilename),
                            name.Substring(0, name.Length - 6) + "cl.xml");


                        Classifier classifier = null;
                        if (selectRootFolderWindow.ViewModel.ClassifierSamplePathVM.FileNameIsValid)
                        {
                            //Если был указан образец структуры, то десериализовать его 
                            //и использовать как основу для для создаваемого классификатора
                            using (StreamReader sr = new StreamReader(
                                selectRootFolderWindow.ViewModel.ClassifierSamplePathVM.FileName))
                            {
                                string serializedData = Common.Utils.RemoveInvalidXmlSubstrs(sr.ReadToEnd());

                                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Classifier));
                                StringReader stringReader = new StringReader(serializedData);
                                classifier = (Classifier)xmlSerializer.Deserialize(stringReader);
                                classifier.ClassName = classifier.Name;
                                classifier.DefaultClasses[0] = classifier.Name + "_DefaultFolder";
                                classifier.DefaultClasses[1] = classifier.Name + "_DefaultGeometry";
                            }
                        }
                        else
                        {
                            //пустой классификатор
                            classifier = new Classifier()
                            {
                                Name = rootFolder.DisplayName,
                                ClassName = rootFolder.DisplayName,
                                IsPrimary = true,
                                DetailLevels = new List<string>() { "Folder", "Geometry" }
                            };
                            classifier.DefaultClasses[0] = rootFolder.DisplayName + "_DefaultFolder";
                            classifier.DefaultClasses[1] = rootFolder.DisplayName + "_DefaultGeometry";
                        }



                        //Создать пустую структуру
                        Structure structure = new Structure()
                        {
                            Name = rootFolder.DisplayName,
                            Classifier = classifier.Name,
                            IsPrimary = true,
                        };
                        //Создать StructureDataStorage
                        StructureDataStorage dataStorage = new StructureDataStorage(
                            doc, stFilename, clFilename, structure, classifier, true,
                            selectRootFolderWindow.SelectedCategories);

                        //Сформировать XML по структуре папок
                        //Каждая папка - объект без свойств
                        StructureFilling(null, rootFolder, dataStorage, doc);

                        dataStorage.SerializeStruture();
                        WinForms.MessageBox.Show("Данные сохранены", "Готово", WinForms.MessageBoxButtons.OK);
                    }


                }
            }
            catch (Exception ex)
            {
                CommonException(ex, "Ошибка при создании файлов структуры из наборов выбора");
            }




            return 0;
        }



        private void StructureFilling(XML.St.Object parent, SavedItem item, StructureDataStorage dataStorage, Document doc)
        {
            if (item is FolderItem)
            {
                FolderItem folder = item as FolderItem;
                XML.St.Object container = dataStorage.CreateNewContainerObject(parent, folder.DisplayName);
                foreach (SavedItem nestedItem in folder.Children)
                {
                    StructureFilling(container, nestedItem, dataStorage, doc);
                }
            }
            else if (item is SelectionSet)
            {
                SelectionSet selectionSet = item as SelectionSet;

                XML.St.Object container = dataStorage.CreateNewContainerObject(parent, selectionSet.DisplayName);

                ModelItemCollection itemsInSelection = selectionSet.GetSelectedItems();

                Search searchForAllIDs = new Search();
                searchForAllIDs.Selection.CopyFrom(itemsInSelection);
                searchForAllIDs.Locations = SearchLocations.DescendantsAndSelf;
                StructureDataStorage.ConfigureSearchForAllGeometryItemsWithIds(searchForAllIDs, false);
                ModelItemCollection selectedGeometry = searchForAllIDs.FindAll(doc, false);
                foreach (ModelItem modelItem in selectedGeometry)
                {
                    dataStorage.CreateNewModelObject(container, modelItem);
                }
            }
        }
    }
}
