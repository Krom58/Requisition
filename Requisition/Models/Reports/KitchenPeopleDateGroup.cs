using System;
using System.Collections.Generic;

namespace Requisition.Models.Reports
{
    public class KitchenPeopleDateGroup
    {
        public DateTime Date { get; set; }
        public string DateDisplay { get; set; } = string.Empty;
        public List<KitchenPeopleReportItem> Items { get; set; } = new();
    }
}
