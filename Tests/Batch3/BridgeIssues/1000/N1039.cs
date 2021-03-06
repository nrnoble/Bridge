using Bridge.Test;

using System;

namespace Bridge.ClientTest.Batch3.BridgeIssues
{
    // Bridge[#1039]
    [Category(Constants.MODULE_ISSUES)]
    [TestFixture(TestNameFormat = "#1039 - {0}")]
    public class Bridge1039
    {
        [Test(ExpectedCount = 1)]
        public static void TestMoreThanDecimalDigitsFromTotalHours()
        {
            var a = new DateTime(2015, 1, 1, 9);
            var b = new DateTime(2015, 1, 1, 12, 52);

            decimal value = (decimal)((b - a).TotalHours);

            Assert.AreEqual("3.866666666666667", value.ToString());
        }
    }
}