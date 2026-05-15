// System
using System.Windows.Forms;

// DataMigrationTool
using Dataverse.XrmTools.DataMigrationTool.Helpers;

// 3rd Party
using Xunit;

namespace Dataverse.XrmTools.DataMigrationTool.Tests
{
    public class ListViewComparerTests
    {
        [Fact]
        public void Compare_SortsNumericValuesAscending()
        {
            var comparer = new ListViewComparer(0, SortOrder.Ascending);

            Assert.True(comparer.Compare(Item("2"), Item("10")) < 0);
        }

        [Fact]
        public void Compare_SortsStringValuesDescending()
        {
            var comparer = new ListViewComparer(0, SortOrder.Descending);

            Assert.True(comparer.Compare(Item("Alpha"), Item("Beta")) > 0);
        }

        [Fact]
        public void Compare_SortsDateValuesAscending()
        {
            var comparer = new ListViewComparer(0, SortOrder.Ascending);

            Assert.True(comparer.Compare(Item("2026-05-15"), Item("2026-05-16")) < 0);
        }

        private static ListViewItem Item(string value)
        {
            return new ListViewItem(value);
        }
    }
}
