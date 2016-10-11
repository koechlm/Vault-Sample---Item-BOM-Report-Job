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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Connectivity.WebServices;

namespace BOMReportJob
{
    class BOMRow
    {
        /// <summary>
        /// the "key" for the BOMRow object
        /// </summary>
        public ItemAssoc ItemAssoc;

        /// <summary>
        /// Occurrence of an item in the BOM
        /// </summary>
        public ItemBOMOcc ItemOcc;

        /// <summary>
        /// The Item obect for the child in ItemAssoc.
        /// Either Item or Component will be null for any given row.
        /// </summary>
        public Item Item;

        /// <summary>
        /// The Component object for the child in ItemAssoc.
        /// Either Item or Component will be null for any given row.
        /// </summary>
        public BOMComp Component;

        /// <summary>
        /// The children of this row
        /// </summary>
        public List<BOMRow> Children;

        public string BOMPath;

        public BOMRow(ItemBOMOcc itemOcc, ItemAssoc itemAssoc, Item item, BOMComp component, string bomPath)
        {
            this.ItemOcc = itemOcc;
            this.ItemAssoc = itemAssoc;
            this.Item = item;
            this.Component = component;
            this.BOMPath = bomPath;
            this.Children = new List<BOMRow>();
        }
    }
}
