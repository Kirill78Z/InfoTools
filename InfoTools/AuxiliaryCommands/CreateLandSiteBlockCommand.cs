﻿using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Gis.Map;
using Autodesk.Gis.Map.Project;
using Autodesk.Gis.Map.ObjectData;
using static Common.ExceptionHandling.ExeptionHandlingProcedures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


[assembly: CommandClass(typeof(Civil3DInfoTools.AuxiliaryCommands.CreateLandSiteBlockCommand))]

namespace Civil3DInfoTools.AuxiliaryCommands
{
    public class CreateLandSiteBlockCommand
    {
        private static short colorIndex = 82;

        /// <summary>
        /// Выбор полилиний, указание текстового примитива, создание блока с этими полилиниями и названием из указанного текста
        /// </summary>
        [CommandMethod("S1NF0_CreateLandSiteBlock", CommandFlags.Modal)]
        public void CreateLandSiteBlock()
        {
            Document adoc = Application.DocumentManager.MdiActiveDocument;
            if (adoc == null) return;

            Database db = adoc.Database;

            Editor ed = adoc.Editor;

            List<Polyline> selectedPolylines = null;

            try
            {
                //Выбор полилиний
                TypedValue[] tv = new TypedValue[]
                {
                new TypedValue(0, "LWPOLYLINE")
                };
                SelectionFilter flt = new SelectionFilter(tv);
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nВыберите полилинии границ участка";
                PromptSelectionResult acSSPrompt = adoc.Editor.GetSelection(pso, flt);

                if (acSSPrompt.Status == PromptStatus.OK)
                {
                    SelectionSet acSSet = acSSPrompt.Value;

                    //Подсветить выбранные полилинии
                    selectedPolylines = new List<Polyline>();
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in acSSet.GetObjectIds())
                        {
                            Polyline polyline = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                            selectedPolylines.Add(polyline);
                        }
                        tr.Commit();
                    }
                    Highlight(selectedPolylines, true);


                    //Указание текстового примитива
                    PromptEntityOptions peo1 = new PromptEntityOptions("\nУкажите текстовый примитив с названием участка");
                    peo1.SetRejectMessage("\nМожно выбрать только поверхность TIN");
                    peo1.AddAllowedClass(typeof(DBText), true);
                    PromptEntityResult per1 = ed.GetEntity(peo1);
                    if (per1.Status == PromptStatus.OK)
                    {
                        //Снять подсветку
                        Highlight(selectedPolylines, false);

                        string blockName = null;

                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            DBText dBText = tr.GetObject(per1.ObjectId, OpenMode.ForRead) as DBText;
                            string initialName = dBText.TextString;
                            //Создать новый блок
                            blockName = Utils.GetSafeSymbolName(initialName);

                            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForWrite) as BlockTable;

                            if (bt.Has(blockName))
                            {
                                //Если блок с таким именем уже есть, то найти такое имя, которого еще нет
                                int i = 0;
                                string nameToCheck = null;
                                do
                                {
                                    nameToCheck = blockName + "_" + i;
                                    i++;
                                }
                                while (bt.Has(nameToCheck));
                                blockName = nameToCheck;
                            }

                            //Создать таблицу map3d для записи правильного названия блока (если еще нет) --- ОТМЕНЕНО, ТАК КАК НЕ ВИДНО В NAVIS
                            //MapApplication mapApp = HostMapApplicationServices.Application;
                            //ProjectModel projModel = mapApp.ActiveProject;
                            //Tables odTables = projModel.ODTables;
                            //Autodesk.Gis.Map.ObjectData.Table odTable = null;
                            ////FieldDefinitions fDefs = null;
                            //if (!odTables.IsTableDefined("ReferenceNumber"))
                            //{
                            //    FieldDefinitions fDefs = mapApp.ActiveProject.MapUtility.NewODFieldDefinitions();
                            //    FieldDefinition def = fDefs.Add("ReferenceNumber", "Условный номер участка",
                            //    Autodesk.Gis.Map.Constants.DataType.Character, 0);

                            //    odTables.Add("ReferenceNumber", fDefs, "", true);
                            //}
                            //odTable = odTables["ReferenceNumber"];



                            //Создание нового блока
                            BlockTableRecord btr = new BlockTableRecord();
                            btr.Name = blockName;
                            ObjectId btrId = bt.Add(btr);
                            tr.AddNewlyCreatedDBObject(btr, true);

                            ObjectId layerId = ObjectId.Null;
                            ObjectIdCollection objectIdCollection = new ObjectIdCollection();
                            //Копирование всех полилиний в созданный блок
                            foreach (ObjectId id in acSSet.GetObjectIds())
                            {
                                Polyline polyline = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                                if (layerId == ObjectId.Null)
                                    layerId = polyline.LayerId;//Запомнить слой.

                                Polyline polylineCopy = (Polyline)polyline.Clone();
                                polylineCopy.Elevation = 0.0;
                                //Поменять цвет на красный
                                polylineCopy.ColorIndex = colorIndex;

                                objectIdCollection.Add(btr.AppendEntity(polylineCopy));
                                tr.AddNewlyCreatedDBObject(polylineCopy, true);
                            }

                            //Создать атрибут внутри блока
                            //using (Transaction trAttr = db.TransactionManager.StartTransaction())
                            using (AttributeDefinition acAttDef = new AttributeDefinition())
                            {
                                acAttDef.Position = dBText.Position;
                                acAttDef.Verifiable = true;
                                acAttDef.Prompt = "Условный номер";
                                acAttDef.Tag = "ReferenceNumber";
                                acAttDef.TextString = initialName;
                                acAttDef.Height = dBText.Height;
                                acAttDef.Justify = dBText.Justify;
                                acAttDef.TextStyleId = dBText.TextStyleId;
                                acAttDef.ColorIndex = colorIndex;
                                acAttDef.WidthFactor = dBText.WidthFactor;
                                acAttDef.LayerId = dBText.LayerId;

                                btr.AppendEntity(acAttDef);
                                tr/*Attr*/.AddNewlyCreatedDBObject(acAttDef, true);
                                //trAttr.Commit();
                            }

                            //Создать штриховку http://adndevblog.typepad.com/autocad/2012/07/hatch-using-the-autocad-net-api.html
                            using (Hatch oHatch = new Hatch())
                            {

                                try
                                {
                                    Vector3d normal = new Vector3d(0.0, 0.0, 1.0);
                                    oHatch.Normal = normal;
                                    oHatch.Elevation = 0.0;
                                    oHatch.PatternScale = objectIdCollection.Count == 1 ? 2.0 : 10.0;
                                    oHatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI37");
                                    oHatch.ColorIndex = colorIndex;

                                    btr.AppendEntity(oHatch);
                                    tr.AddNewlyCreatedDBObject(oHatch, true);

                                    oHatch.Associative = true;
                                    foreach (ObjectId id in objectIdCollection)
                                    {
                                        oHatch.AppendLoop((int)HatchLoopTypes.Default, new ObjectIdCollection() { id });
                                    }

                                    oHatch.EvaluateHatch(true);
                                }
                                catch (System.Exception)
                                {
                                    try { oHatch.Erase(true); } catch { }
                                }
                            }


                            //Создание вхождения этого блока
                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                            BlockReference br = new BlockReference(Point3d.Origin, btrId);
                            br.LayerId = layerId;
                            ObjectId brId = ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);


                            //Заполнить значение атрибута
                            AttributeCollection ac = br.AttributeCollection;
                            //AttributeReference ar = (AttributeReference)tr.GetObject(ac[0], OpenMode.ForWrite);
                            //ar.TextString = initialName;

                            //Привязать к вхождению блока запись таблицы map3d --- ОТМЕНЕНО, ТАК КАК НЕ ВИДНО В NAVIS
                            //Records odrecords = odTable.GetObjectTableRecords(Convert.ToUInt32(0), brId,
                            //                                        Autodesk.Gis.Map.Constants.OpenMode.OpenForRead, false);
                            //Record odRecord = Autodesk.Gis.Map.ObjectData.Record.Create();
                            //odTable.InitRecord(odRecord);

                            //Autodesk.Gis.Map.Utilities.MapValue mapVal = odRecord[0];
                            //mapVal.Assign(name);
                            //odTable.AddRecord(odRecord, br);


                            tr.Commit();
                        }

                        if (!String.IsNullOrEmpty(blockName))
                        {
                            adoc.SendStringToExecute("_ATTSYNC _N " + blockName + "\n", false, false, false);
                        }



                        adoc.SendStringToExecute("S1NF0_CreateLandSiteBlock\n", false, false, false);

                    }

                }
            }
            catch (System.Exception ex)
            {
                //Utils.ErrorToCommandLine(ed, "Ошибка при создании контуров разметки", ex);
                CommonException(ex, "Ошибка при создании контуров разметки");
            }
            finally
            {
                //Снять подсветку
                Highlight(selectedPolylines, false);
            }
        }


        private void Highlight(List<Polyline> selectedPolylines, bool yes)
        {
            if (selectedPolylines != null)
                foreach (Polyline p in selectedPolylines)
                {
                    if (yes)
                    {
                        p.Highlight();
                    }
                    else
                    {
                        p.Unhighlight();
                    }
                }
        }
    }
}