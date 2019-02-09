/*=====================================================================
  
  This file is part of the Autodesk Vault API Code Samples.

  Copyright (C) Autodesk Inc.  All rights reserved.

THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
PARTICULAR PURPOSE.
=====================================================================*/

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;

using Microsoft.Reporting.WinForms;

using Autodesk.Connectivity.Extensibility.Framework;
using Autodesk.Connectivity.JobProcessor.Extensibility;
using Autodesk.Connectivity.WebServices;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections;
using Autodesk.DataManagement.Client.Framework.Vault.Currency.Properties;
using Autodesk.DataManagement.Client.Framework.Vault.Settings;
using Autodesk.DataManagement.Client.Framework.Currency;
using VDF = Autodesk.DataManagement.Client.Framework;

[assembly: ApiVersion("12.0")]
[assembly: ExtensionId("267602E2-5DCE-46A5-85A8-3A26FD76D0B5")]

namespace BOMReportJob
{
    public class JobExtension : IJobHandler
    {
        private static string JOB_TYPE = "Autodesk.BOMReport";
        private static string DATA_SOURCE_NAME = "AutodeskVault_ReportDataSource";

        #region IJobHandler implementation
        public bool CanProcess(string jobType)
        {
            return jobType == JOB_TYPE;
        }

        public JobOutcome Execute(IJobProcessorServices context, IJob job)
        {
            try
            {
                GenerateBOMReport(job, context.Connection);

                //return JobOutcome.Failure;  // for debugging purposes
                return JobOutcome.Success;
            }
            catch (Exception ex)
            {
                context.Log(ex, "BOM Report job failure: " + ex.ToString() + " ") ;
                return JobOutcome.Failure;
            }
        }

        public void OnJobProcessorShutdown(IJobProcessorServices context)
        {
            // do nothing
        }

        public void OnJobProcessorSleep(IJobProcessorServices context)
        {
            // do nothing
        }

        public void OnJobProcessorStartup(IJobProcessorServices context)
        {
            // do nothing
        }

        public void OnJobProcessorWake(IJobProcessorServices context)
        {
            // do nothing
        }
        #endregion IJobHandler implementation

        private void GenerateBOMReport(IJob job, Connection conn)
        {
            long itemId = long.Parse(job.Params["EntityId"]);
            
            string entityClassId = job.Params["EntityClassId"];
            long lcTransId = long.Parse(job.Params["LifeCycleTransitionId"]);

            // Only run the job for Items
            if (entityClassId != "ITEM")
                return;

            LfCycTrans trasition = conn.WebServiceManager.LifeCycleService.GetLifeCycleStateTransitionsByIds(lcTransId.ToSingleArray()).First();
            LfCycState state = conn.WebServiceManager.LifeCycleService.GetLifeCycleStatesByIds(trasition.ToId.ToSingleArray()).First();

            // only run the job when releasing an Item
            if (!state.ReleasedState)
                return;

            Settings settings = Settings.Load();
            if (settings.ReportsVaultPath == null)
                throw new Exception("reportsVaultPath has not been configured in settings file"); 
            if (!settings.ReportsVaultPath.EndsWith("/"))
                settings.ReportsVaultPath = settings.ReportsVaultPath + "/";

            DataTable dataTable = new DataTable("ReportDataSet");
            Dictionary<string, ReportParameter> reportParams = new Dictionary<string, ReportParameter>();
            Item rootItem = ReadBOMData(dataTable, reportParams, itemId, settings.BomType, conn);
            
            //Item rootItem = ReadBOMData(dataTable, reportParams, itemMasterId, settings.BomType, conn);

            //if (!settings.GeneratePartReports && dataTable.Rows.Count <= 1)
            if (!settings.GeneratePartReports && dataTable.Rows.Count <= 1) //changed with build 24.0.0.1
                    return;  // no BOM data

            string rdlcPath = System.IO.Path.Combine(Util.GetAssemblyPath(), settings.ReportTemplate);
            string outPath = null;
            if (settings.ReportOutFormat == "PDF") 
            {
                outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), rootItem.ItemNum + ".pdf");
            }
            if (settings.ReportOutFormat == "XLS")
            {
                outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), rootItem.ItemNum + ".xls");
            }

            LocalReport report = new LocalReport();
            report.ReportPath = rdlcPath;
            report.DataSources.Clear();
            ReportDataSource ds = new ReportDataSource(DATA_SOURCE_NAME, dataTable);
            report.DataSources.Add(ds);
            
            ReportParameterInfoCollection rdlcParams = report.GetParameters();
            List<ReportParameter> usedParams = new List<ReportParameter>();
            foreach (ReportParameterInfo rdlcParam in rdlcParams)
            {
                ReportParameter theParam = null;
                if (reportParams.TryGetValue(rdlcParam.Name, out theParam))
                    usedParams.Add(theParam);
            }

            report.SetParameters(usedParams);
            byte[] bytes = null;
            if (settings.ReportOutFormat == "PDF")
            {
                bytes = report.Render("PDF");
            }

            if (settings.ReportOutFormat == "XLS")
            {
                string mimeType;
                string encoding;
                string extension;
                string[] streamids;
                Warning[] warnings;
                bytes = report.Render("Excel", null, out mimeType, out encoding, out extension, out streamids, out warnings);
            }
            
            if (System.IO.File.Exists(outPath))
                System.IO.File.Delete(outPath);

            System.IO.File.WriteAllBytes(outPath, bytes);

            string vaultFilePath = System.IO.Path.Combine(settings.ReportsVaultPath, System.IO.Path.GetFileName(outPath));
            File wsFile = conn.WebServiceManager.DocumentService.FindLatestFilesByPaths(vaultFilePath.ToSingleArray()).First();
            FilePathAbsolute vdfPath = new FilePathAbsolute(outPath);
            VDF.Vault.Currency.Entities.FileIteration vdfFile = null;
            VDF.Vault.Currency.Entities.FileIteration addedFile = null;
            VDF.Vault.Currency.Entities.FileIteration mUploadedFile = null;
            if (wsFile == null || wsFile.Id < 0)
            {
                // upload file to Vault
                Folder vaultFolder = conn.WebServiceManager.DocumentService.FindFoldersByPaths(settings.ReportsVaultPath.ToSingleArray()).First();
                if (vaultFolder == null || vaultFolder.Id == -1)
                    throw new Exception("Vault reports folder " + settings.ReportsVaultPath + " not found");

                var folderEntity = new Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities.Folder(conn, vaultFolder);
                try
                {
                    addedFile = conn.FileManager.AddFile(folderEntity, "Created by Job Processor", null, null, FileClassification.None, false, vdfPath);
                }
                catch
                {
                    throw new Exception("Job could not add report file " + vdfPath);
                }

            }
            else
            {
                // checkin new file version
                AcquireFilesSettings aqSettings = new AcquireFilesSettings(conn)
                {
                    DefaultAcquisitionOption = AcquireFilesSettings.AcquisitionOption.Checkout
                };
                vdfFile = new VDF.Vault.Currency.Entities.FileIteration(conn, wsFile);
                aqSettings.AddEntityToAcquire(vdfFile);
                var results = conn.FileManager.AcquireFiles(aqSettings);
                try
                {
                    mUploadedFile = conn.FileManager.CheckinFile(results.FileResults.First().File, "Created by Job Processor", false, null, null, false, null, FileClassification.None, false, vdfPath);
                }
                catch
                {
                    throw new Exception("Job could not update existing report file " + vdfFile);
                }
            }

                // download the file to given location
                if (settings.ReportOutExtLocation != "")
                {
                    VDF.Vault.Settings.AcquireFilesSettings mDownloadSettings = new VDF.Vault.Settings.AcquireFilesSettings(conn);
                    mDownloadSettings.LocalPath = new VDF.Currency.FolderPathAbsolute(settings.ReportOutExtLocation);
                    try
                    {
                        if (mUploadedFile != null)
                            mDownloadSettings.AddFileToAcquire(mUploadedFile, VDF.Vault.Settings.AcquireFilesSettings.AcquisitionOption.Download);
                        if (addedFile != null)
                            mDownloadSettings.AddFileToAcquire(addedFile, VDF.Vault.Settings.AcquireFilesSettings.AcquisitionOption.Download);

                        VDF.Vault.Results.AcquireFilesResults mDownLoadResupt = conn.FileManager.AcquireFiles(mDownloadSettings);
                    }
                    catch (Exception)
                    {
                        throw new Exception("Job could not download report file" + vdfFile);
                    }
                }
                
            // delete temp report
            System.IO.File.SetAttributes(outPath, System.IO.FileAttributes.Normal);
            System.IO.File.Delete(outPath);

            // update item if settings.attachUpdateReport = true
            if (settings.attachUpdateReport)
            {
                // update item - even if we already have the report attached, we still need to update the item so that it picks up the latest version
                Item latestItem = conn.WebServiceManager.ItemService.GetLatestItemByItemMasterId(rootItem.MasterId);
                Item editableItem = latestItem;
                long origState = latestItem.LfCyc.LfCycStateId;
                if (latestItem.LfCyc.Consume)
                {
                    LfCycDef lcDef = conn.WebServiceManager.LifeCycleService.GetLifeCycleDefinitionsByIds(
                        latestItem.LfCyc.LfCycDefId.ToSingleArray()).First();

                    LfCycState changeState = lcDef.StateArray.FirstOrDefault(n =>
                        n.Name.Equals(settings.QuickChangeState, StringComparison.InvariantCultureIgnoreCase) ||
                        n.DispName.Equals(settings.QuickChangeState, StringComparison.InvariantCultureIgnoreCase));
                    if (changeState == null)
                        throw new Exception("No lifecycle state found called " + settings.QuickChangeState);

                    LfCycTrans transition = lcDef.TransArray.FirstOrDefault(n => n.FromId == latestItem.LfCyc.LfCycStateId && n.ToId == changeState.Id);
                    if (transition == null)
                        throw new Exception("No lifecycle transition found to state " + settings.QuickChangeState);
                    try
                    {
                        editableItem = conn.WebServiceManager.ItemService.UpdateItemLifeCycleStates(latestItem.MasterId.ToSingleArray(),
                            changeState.Id.ToSingleArray(), "Attaching BOM report").First();
                    }
                    catch
                    {
                        throw new Exception("Item's state could not be changed, e.g. check job user's rights for transition to target state.");
                    }

                }

                if (addedFile != null)
                {
                    ItemAttmt[] attachments = conn.WebServiceManager.ItemService.GetAttachmentsByItemIds(editableItem.Id.ToSingleArray());
                    List<Attmt> attachments2 = null;
                    if (attachments.Length == 1)
                    {
                        attachments2 = attachments[0].AttmtArray.ToList();
                    }
                    else
                        attachments2 = new List<Attmt>();
                    try
                    {
                        attachments2.Add(new Attmt() { FileId = addedFile.EntityIterationId, Pin = false });
                        editableItem = conn.WebServiceManager.ItemService.UpdateAttachments(editableItem.RevId, attachments2.ToArray());
                    }
                    catch
                    {
                        throw new Exception("Item's attached BOM report could not get attached. Check that items are not locked for the job user in the given state Settings.quickChangeState");
                    }

                }
                else
                {
                    editableItem = conn.WebServiceManager.ItemService.EditItems(editableItem.RevId.ToSingleArray()).First();
                }

                try
                {
                    conn.WebServiceManager.ItemService.UpdateAndCommitItems(editableItem.ToSingleArray());
                }
                catch (Exception)
                {
                    throw new Exception("Item's attached BOM report could not get updated. Check that items are not locked for the job user in the given state Settings.quickChangeState");
                }    
                
                if (editableItem.LfCyc.LfCycStateId != origState)
                {
                    conn.WebServiceManager.ItemService.UpdateItemLifeCycleStates(latestItem.MasterId.ToSingleArray(),
                        origState.ToSingleArray(), "Moving back to original state after attaching BOM report").First();
                }
            }


        }

        private BOMRow ConstructBOM(long rootItemId, ItemBOM bom)
        {
            // optimize for quick lookup
            Dictionary<long, Item> itemMap = bom.ItemRevArray.ToDictionary(n => n.Id);

            Dictionary<long, BOMComp> compMap = new Dictionary<long, BOMComp>();
            if (bom.BOMCompArray != null)
                compMap = bom.BOMCompArray.ToDictionary(n => n.Id);

            // key = parent Item Id;  value = associations where parent Item Id matches the key
            Dictionary<long, List<ItemAssoc>> itemAssocMap = new Dictionary<long, List<ItemAssoc>>();

            if (bom.ItemAssocArray != null)
            {
                foreach (ItemAssoc itemAssoc in bom.ItemAssocArray)
                {
                    List<ItemAssoc> assocs = null;
                    if (itemAssocMap.TryGetValue(itemAssoc.ParItemID, out assocs))
                        assocs.Add(itemAssoc);
                    else
                    {
                        assocs = new List<ItemAssoc>();
                        assocs.Add(itemAssoc);
                        itemAssocMap.Add(itemAssoc.ParItemID, assocs);
                    }
                }
            }

            Stack<BOMRow> rowStack = new Stack<BOMRow>();

            Item rootItem = itemMap[rootItemId];
            BOMRow root = new BOMRow(null, null, rootItem, null, "");
            rowStack.Push(root);

            while (rowStack.Any())
            {
                BOMRow row = rowStack.Pop();
                List<ItemAssoc> assocs = null;
                if (row.Item != null)
                    itemAssocMap.TryGetValue(row.Item.Id, out assocs);

                if (assocs == null)
                    continue;

                foreach (ItemAssoc assoc in assocs.OrderBy(n => n.BOMOrder))
                {
                    // try to find the occ by the path
                    string path = row.BOMPath + "/" + assoc.AssocMasterId.ToString();
                    path = path.Trim('/'.ToSingleArray());

                    ItemBOMOcc occ = null;

                    if (bom.OccurArray != null)
                    {
                        // look up the occurrence by path
                        occ = bom.OccurArray.FirstOrDefault(n => n.Path == path);
                        if (occ == null)
                        {
                            // look up the occurrence by assocMasterId
                            occ = bom.OccurArray.FirstOrDefault(n => n.Path == null && n.AssocMasterId == assoc.AssocMasterId);
                        }
                    }

                    Item childItem = null;
                    itemMap.TryGetValue(assoc.CldItemID, out childItem);
                    BOMComp childComp = null;
                    compMap.TryGetValue(assoc.BOMCompId, out childComp);

                    BOMRow newRow = new BOMRow(occ, assoc, childItem, childComp, path);
                    row.Children.Add(newRow);
                    rowStack.Push(newRow);
                }
            }

            return root;
        }

        private Item ReadBOMData(DataTable dataTable, Dictionary<string, ReportParameter> reportParams, 
            long itemId, BOMType bomType, Connection conn)
        {
            long[] m_itemIds = new long[1];
            m_itemIds[0] = itemId;
            Item[] m_Items = conn.WebServiceManager.ItemService.GetItemsByIds(m_itemIds);
            Item m_Item = m_Items[0];
            long itemMasterId = m_Item.MasterId;
            Item rootItem = conn.WebServiceManager.ItemService.GetLatestItemByItemMasterId(itemMasterId);
            
            // set up the item proeprty columns
            PropertyDefinitionDictionary itemPropDefs = conn.PropertyManager.GetPropertyDefinitions("ITEM", null, 
                Autodesk.DataManagement.Client.Framework.Vault.Currency.Properties.PropertyDefinitionFilter.IncludeAll);
            List<PropertyDefinition> serverProps = itemPropDefs.Values.Where(n => !n.IsCalculated).ToList();
            foreach (PropertyDefinition itemPropDef in serverProps)
            {
                dataTable.Columns.Add(itemPropDef.SystemName, GetDataType(itemPropDef));
            }

            // set up the bom row property columns
            AssocPropDef[] bomRowProps = conn.WebServiceManager.PropertyService.GetAssociationPropertyDefinitionsByType(AssocPropTyp.ItemBOMAssoc);
            foreach (AssocPropDef assocPropDef in bomRowProps)
            {
                dataTable.Columns.Add(assocPropDef.SysName, GetDataType(assocPropDef.Typ));
            }
            Dictionary<long, AssocPropDef> assocPropDefMap = bomRowProps.ToDictionary(n => n.Id);

            // special cases
            dataTable.Columns.Add("ItemBomOrder_BomRow", typeof(string));
            dataTable.Columns.Add("PositionNumber_BomRow", typeof(string));
            dataTable.Columns.Add("ItemQty_BomRow", typeof(string));
            dataTable.Columns.Add("ItemIncluded_BomRow", typeof(string));
            dataTable.Columns.Add("LatestObsolete_BomRow", typeof(string));
            dataTable.Columns.Add("ItemDetailId_BomRow", typeof(string));
            dataTable.Columns.Add("ItemAssocFile_BomRow", typeof(string));
            dataTable.Columns.Add("ItemAssocFileVersion_BomRow", typeof(string));
            dataTable.Columns.Add("GroupType", typeof(string));
            dataTable.Columns.Add("RowType", typeof(string));
            dataTable.Columns.Add("UnitQty", typeof(string));
            dataTable.Columns.Add("ItemQty", typeof(long));


            // get historical
            ItemBOM bom = null;
            if (bomType == BOMType.MULTI_LEVEL)
            {
                bom = conn.WebServiceManager.ItemService.GetItemBOMByItemIdAndDate(itemId, rootItem.LastModDate, BOMTyp.Historic,
                    BOMViewEditOptions.Defaults |
                    BOMViewEditOptions.ReturnOccurrences |  // needed to get the detail ID
                    BOMViewEditOptions.ReturnExcluded |  // off rows that are items 
                    BOMViewEditOptions.ReturnUnassignedComponents  // off rows that are components
                    );
            }
            else if (bomType == BOMType.FIRST_LEVEL)
            {
                bom = conn.WebServiceManager.ItemService.GetItemBOMByItemIdAndDate(itemId, rootItem.LastModDate, BOMTyp.Historic,
                    BOMViewEditOptions.ReturnOneLevelAtATime |
                    BOMViewEditOptions.ReturnOccurrences |  // needed to get the detail ID
                    BOMViewEditOptions.ReturnExcluded |  // off rows that are items 
                    BOMViewEditOptions.ReturnUnassignedComponents  // off rows that are components
                    );
            }
            else if (bomType == BOMType.PARTS_ONLY)
            {
                bom = conn.WebServiceManager.ItemService.GetItemBOMByItemIdAndDate(itemId, rootItem.LastModDate, BOMTyp.Historic,
                    BOMViewEditOptions.BOMRolledUp |
                    BOMViewEditOptions.ReturnOccurrences |  // needed to get the detail ID
                    BOMViewEditOptions.ReturnExcluded |  // off rows that are items 
                    BOMViewEditOptions.ReturnUnassignedComponents  // off rows that are components
                    );
            }

            // set up the rows
            long [] itemIds = bom.ItemRevArray.Select(n => n.Id).ToArray();
            var vdfItems = conn.ItemManager.GetItemsByIterationId(itemIds);
            PropertyValues propValues = conn.PropertyManager.GetPropertyValues(vdfItems.Values, serverProps, null);
            Item [] latestItems = conn.WebServiceManager.ItemService.GetLatestItemsByItemMasterIds(
                bom.ItemRevArray.Select(n => n.MasterId).ToArray());
            Dictionary<long, Item> latestItemMap = latestItems.ToDictionary(n => n.MasterId);

            ItemAssocProp [] assocProps = new ItemAssocProp[0];
            if (!bom.ItemAssocArray.IsNullOrEmpty() && !bomRowProps.IsNullOrEmpty())
            {
                assocProps = conn.WebServiceManager.ItemService.GetItemBOMAssociationProperties(
                    bom.ItemAssocArray.Select(n => n.Id).ToArray(), 
                    bomRowProps.Select(n => n.Id).ToArray());
            }

            ItemFileAssoc [] fileAssocs = conn.WebServiceManager.ItemService.GetItemFileAssociationsByItemIds(itemIds, 
                ItemFileLnkTypOpt.Primary | ItemFileLnkTypOpt.PrimarySub);
            Dictionary<long, ItemFileAssoc> fileAssocMap = fileAssocs.ToDictionary(n => n.ParItemId);

            BOMRow root = ConstructBOM(itemId, bom);
            ReadBOMRow(dataTable, reportParams, root, vdfItems, propValues, assocProps, assocPropDefMap, "", latestItemMap, fileAssocMap, conn);

            if (bomType == BOMType.FIRST_LEVEL || bomType == BOMType.PARTS_ONLY)
                dataTable.Rows.RemoveAt(0);  // remove the root node

            // special params
            AddReportParam("BOMView_EffectiveDate", DateTime.Now.ToString(), reportParams);
            AddReportParam("BOMView_IsTipRevision", true.ToString(), reportParams);
            AddReportParam("Vault_UserName", conn.UserName, reportParams);
            AddReportParam("Vault_VaultName", conn.Vault, reportParams);

            return rootItem;
        }

        private void AddReportParam(string name, string value, Dictionary<string, ReportParameter> reportParams)
        {
            ReportParameter param = new ReportParameter(name, value);
            reportParams.Add(param.Name, param);
        }

        private void ReadBOMRow(DataTable dataTable, Dictionary<string, ReportParameter> reportParams, 
            BOMRow bomRow, IDictionary<long, Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities.ItemRevision> vdfItems, 
            PropertyValues propValues, ItemAssocProp [] assocProps, Dictionary<long, AssocPropDef> assocPropDefMap, string rowOrder, 
            Dictionary<long, Item> latestItemMap, Dictionary<long, ItemFileAssoc> fileAssocMap,
            Connection conn)
        {
            Dictionary<PropertyDefinition, PropertyValue> values = new Dictionary<PropertyDefinition, PropertyValue>();
            if (bomRow.Item != null)
                values = propValues.GetValues(vdfItems[bomRow.Item.Id]);
            
            DataRow dataRow = dataTable.NewRow();

            // set item properties
            foreach (KeyValuePair<PropertyDefinition, PropertyValue> value in values)
            {
                object valueObj = value.Value.Value;

                if (valueObj == null)
                    valueObj = DBNull.Value;
                else if (value.Key.DataType == PropertyDefinition.PropertyDataType.Image)
                {
                    ThumbnailInfo thumbnail = valueObj as ThumbnailInfo;
                    valueObj = Convert.ToBase64String(thumbnail.Image);
                }
                else if (value.Key.DataType == PropertyDefinition.PropertyDataType.ImageInfo  && valueObj != null)
                {
                    try
                    {
                        ImageInfo imageVal = valueObj as ImageInfo;
                        valueObj = ConvertImageToBase64(imageVal.GetImage());
                    }
                    catch
                    {
                        valueObj = DBNull.Value;
                    }
                }

                string sysName = value.Key.SystemName;
                dataRow[sysName] = valueObj;

                if (bomRow.ItemAssoc == null && valueObj != DBNull.Value)
                {
                    // if it's the root node, set the parameter data
                    string paramDispName = ConvertParamName(value.Key.DisplayName, true);
                    string paramSysName = ConvertParamName(value.Key.SystemName, true);

                    reportParams.Add(paramSysName, new ReportParameter(paramSysName, valueObj.ToString()));
                    if (!reportParams.ContainsKey(paramDispName))
                        reportParams.Add(paramDispName, new ReportParameter(paramDispName, valueObj.ToString()));
                }
            }

            // set bom properties
            if (bomRow.ItemAssoc != null && assocProps != null)
            {
                var matchingProps = assocProps.Where(n => n.AssocId == bomRow.ItemAssoc.Id);
                foreach (ItemAssocProp prop in matchingProps)
                {
                    AssocPropDef propDef;
                    if (assocPropDefMap.TryGetValue(prop.PropDefId, out propDef))
                    {
                        string escSysName = ConvertParamName(propDef.SysName, false);
                        dataRow[propDef.SysName] = prop.Val;
                    }
                }
            }

            // special cases
            if (bomRow.ItemAssoc == null)
            {
                // most data is blank for the root
                dataRow["ItemBomOrder_BomRow"] = "-";
                dataRow["PositionNumber_BomRow"] = "-";
                dataRow["ItemQty_BomRow"] = "-";
                dataRow["ItemIncluded_BomRow"] = true.ToString();
                dataRow["UnitQty"] = "-";
                dataRow["ItemDetailId_BomRow"] = "-";
                dataRow["RowType"] = "-";
                dataRow["GroupType"] = "-";
            }
            else
            {
                if (rowOrder.Length == 0)
                    rowOrder = bomRow.ItemAssoc.BOMOrder.ToString();
                else
                    rowOrder = rowOrder + "." + bomRow.ItemAssoc.BOMOrder.ToString();
                dataRow["ItemBomOrder_BomRow"] = rowOrder;

                dataRow["PositionNumber_BomRow"] = bomRow.ItemAssoc.PositionNum;
                dataRow["ItemQty_BomRow"] = bomRow.ItemAssoc.Quant.ToString();
                dataRow["ItemIncluded_BomRow"] = bomRow.ItemAssoc.IsIncluded.ToString();

                if (bomRow.ItemAssoc.UnitSizeSpecified)
                    dataRow["UnitQty"] = bomRow.ItemAssoc.UnitSize.ToString();

                if (bomRow.ItemAssoc.InstCountSpecified)
                    dataRow["ItemQty"] = bomRow.ItemAssoc.InstCount;

                if (bomRow.ItemOcc != null)
                    dataRow["ItemDetailId_BomRow"] = bomRow.ItemOcc.Val;

                if (bomRow.ItemAssoc.IsCad)
                    dataRow["RowType"] = Resource.rowTypeCad;
                else
                    dataRow["RowType"] = Resource.rowTypeManual;

                if (bomRow.ItemAssoc.IsGroup)
                {
                    if (bomRow.ItemAssoc.IsCad)
                        dataRow["GroupType"] = Resource.groupTypeSystem;
                    else
                        dataRow["GroupType"] = Resource.groupTypeManual;
                }
                
            }

            if (bomRow.Item != null)
            {
                bool latestObsolete = latestItemMap[bomRow.Item.MasterId].LfCyc.Obsolete;
                dataRow["LatestObsolete_BomRow"] = latestObsolete.ToString();

                ItemFileAssoc fileAssoc = null;
                if (fileAssocMap.TryGetValue(bomRow.Item.Id, out fileAssoc))
                {
                    dataRow["ItemAssocFile_BomRow"] = fileAssoc.FileName;
                    dataRow["ItemAssocFileVersion_BomRow"] = fileAssoc.CldFileVerNum;
                }
            }
            else if (bomRow.Component != null)
            {
                dataRow["Number"] = bomRow.Component.Name;
            }

            dataTable.Rows.Add(dataRow);

            foreach (BOMRow child in bomRow.Children)
            {
                ReadBOMRow(dataTable, reportParams, child, vdfItems, propValues, assocProps, assocPropDefMap, rowOrder, latestItemMap, fileAssocMap, conn);
            }
        }

        /// <summary>
        /// Escape the display name of the property.
        /// </summary>
        private string ConvertParamName(string displayName, bool includePrefix)
        {
            char escape = '_';
            char clsCompliantLeadChar = 'a';

            string encodedString = "";

            foreach (char c in displayName)
            {
                if (char.IsLetter(c) ||
                    char.IsNumber(c) ||
                    (c == escape))
                {
                    encodedString += c;
                }
                else 
                {
                    encodedString += escape;
                }
            }

            if (!char.IsLetter(encodedString, 0))
            {
                encodedString = clsCompliantLeadChar + encodedString;
            }

            if (includePrefix)
                encodedString = "ItemBOM_" + encodedString;
            return encodedString;
        }

        private string ConvertImageToBase64(Image image)
        {
            byte[] imageArray;
            using (System.IO.MemoryStream imageStream = new System.IO.MemoryStream())
            {
                // Save to stream as jpeg
                image.Save(imageStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                imageArray = new byte[imageStream.Length];
                imageStream.Seek(0, System.IO.SeekOrigin.Begin);
                imageStream.Read(imageArray, 0, (int)imageStream.Length);
            }

            return Convert.ToBase64String(imageArray);
        }


        private static bool getThumbnailImageAbort()
        {
            return false;
        }

        public Type GetDataType(PropertyDefinition propDef)
        {
            if (!propDef.ValueList.IsNullOrEmpty())
                return typeof(System.String);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.Bool)
                return typeof(System.Boolean);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.Numeric)
                return typeof(System.Double);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.DateTime)
                return typeof(System.DateTime);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.Image)
                return typeof(System.String);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.ImageInfo)
                return typeof(System.String);
            else if (propDef.DataType == PropertyDefinition.PropertyDataType.String)
                return typeof(System.String);

            throw new Exception("Unknown data type for property " + propDef.DisplayName);
        }

        public Type GetDataType(DataType dataType)
        {
            if (dataType == DataType.Bool)
                return typeof(System.Boolean);
            else if (dataType == DataType.Numeric)
                return typeof(System.Double);
            else if (dataType == DataType.DateTime)
                return typeof(System.DateTime);
            else if (dataType == DataType.Image)
                return typeof(System.String);
            else if (dataType == DataType.String)
                return typeof(System.String);

            throw new Exception("Unknown data type " + dataType);
        }
    }
}
