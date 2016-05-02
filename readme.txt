BOM REPORT JOB

INTRODUCTION:
---------------------------------
This utility generates BOM reports through Job Processor.  The reports are rendered as PDF files and attached to the item.

REQUIREMENTS:
---------------------------------
- Vault Professional 2016
- Must have job queue enabled.
- Must have a “Quick Change” (name to be provided in settings.xml file) state that the plug-in can use to move the item to edit mode.
- Must have a folder in Vault for storing the generated reports.

TO CONFIGURE:
---------------------------------
1. Run the install.  
2. Go to the install location, which is %ProgramData%\Autodesk\Vault 2016\Extensions\BOMReportJob.
3. Copy to this folder the RDLC file you want to use when generating reports.  You can skip this step if you want to use one of the default RDLC files which are already in the BOMReportJob folder.
4. Open Settings.xml in a text or XML editor.  Set all the values according to your configuration.  Instructions are in the XML file.
5. Save Settings.xml and exit the editor.
6. Exit Job Processor if it is running.
7. Start Job Processor.
8. Go to Administration->Job Types.  You should see and entry for  “Autodesk.BOMReport”.
9. Run the Lifecycle Event Editor and set “Autodesk.BOMReport” as the Job Type on transitions where you want reports to be created.
9.a If you don’t have the Lifecycle Event Editor, install the SDK.  The SDK installer can be found in the SDK folder of your Vault install path.  Once the SDK is installed, the Lifecycle Event Editor will be in the “util” folder under the SDK install path.
10. Start Vault Explorer.
11. Run an assembly through one of the configured lifecycle transitions.
12. Check the job queue.  You should see an Autodesk.BOMReport for each item in the assembly.
13. After the Job Processor handles the jobs, the queue should be empty.  The assembly items should have Reports attached to them.

FEATURES:
---------------------------------
- BOMReportJobs can be triggered from changes to item lifecycle
- There are three formats that can be used when printing out BOM information:
-- MULTI_LEVEL - This is the full, ordered BOM tree.
-- FIRST_LEVEL - This is for all items immeditaly belows the root.  Anyting deeper in the BOM is omitted.  Order is not guaranteed.
-- PARTS_ONLY - This only shows the leaf nodes of the BOM tree.  Order is not guaranteed.

NOTES:
---------------------------------
- Only one RDLC file can be used.
- PDF files will be named after the Item Number off of the corresponding Item.

RELEASE NOTES:
---------------------------------
1.0.3.0 - Initial Release 2015 R2
21.0.3.0 - Updated Release 2016, Markus Koechl - Changes according API change: GetLatestItemInRevisionByItemId(itemID)=removed; new: GetLatestItemByItemMasterId(itemMasterId) additional code to retrieve itemMasterId from job's itemId