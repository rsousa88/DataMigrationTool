// System
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

            var result = valueA.CompareTo(valueB);
            return _order.Equals(SortOrder.Ascending) ? result : -result;
        }
    }
}
