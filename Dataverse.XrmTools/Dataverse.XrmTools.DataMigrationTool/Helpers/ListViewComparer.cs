// System
using System;
using System.Collections;
using System.Windows.Forms;

namespace Dataverse.XrmTools.DataMigrationTool.Helpers
{
    public class ListViewComparer : IComparer
    {
        private int _columnNumber;
        private SortOrder _order;

        public ListViewComparer(int columnNumber, SortOrder order)
        {
            _columnNumber = columnNumber;
            _order = order;
        }

        public int Compare(object x, object y)
        {
            var itemA = x as ListViewItem;
            var itemB = y as ListViewItem;

            var valueA = itemA.SubItems.Count <= _columnNumber ? string.Empty : itemA.SubItems[_columnNumber].Text;
            var valueB = itemB.SubItems.Count <= _columnNumber ? string.Empty : itemB.SubItems[_columnNumber].Text;

            var result = CompareValues(valueA, valueB);
            return _order.Equals(SortOrder.Ascending) ? result : -result;
        }

        private int CompareValues(string valueA, string valueB)
        {
            if (decimal.TryParse(valueA, out var numberA) && decimal.TryParse(valueB, out var numberB))
                return numberA.CompareTo(numberB);

            if (DateTime.TryParse(valueA, out var dateA) && DateTime.TryParse(valueB, out var dateB))
                return dateA.CompareTo(dateB);

            return string.Compare(valueA, valueB, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
