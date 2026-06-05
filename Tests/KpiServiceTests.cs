using Xunit;
using КР_Ханников.Services;

namespace КР_Ханников.Tests
{
    /// <summary>
    /// Unit-тесты для модуля расчёта показателей эффективности (KpiService).
    /// Проверяют корректность формул KPI и устойчивость к граничным
    /// случаям — нулевым делителям и пустым входным данным.
    /// </summary>
    public class KpiServiceTests
    {
        // ===== Тесты SLA-compliance =====

        [Fact]
        public void CalculateSlaPercent_AllInTime_Returns100()
        {
            double result = KpiService.CalculateSlaPercent(10, 10);
            Assert.Equal(100.0, result);
        }

        [Fact]
        public void CalculateSlaPercent_HalfInTime_Returns50()
        {
            double result = KpiService.CalculateSlaPercent(5, 10);
            Assert.Equal(50.0, result);
        }

        [Fact]
        public void CalculateSlaPercent_NoneInTime_Returns0()
        {
            double result = KpiService.CalculateSlaPercent(0, 10);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateSlaPercent_ZeroTotal_ReturnsZeroWithoutException()
        {
            double result = KpiService.CalculateSlaPercent(0, 0);
            Assert.Equal(0.0, result);
        }

        // ===== Тесты Overdue Rate =====

        [Fact]
        public void CalculateOverdueRate_NoOverdue_Returns0()
        {
            double result = KpiService.CalculateOverdueRate(0, 20);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateOverdueRate_OneFifthOverdue_Returns20()
        {
            double result = KpiService.CalculateOverdueRate(4, 20);
            Assert.Equal(20.0, result);
        }

        [Fact]
        public void CalculateOverdueRate_ZeroTotal_ReturnsZeroWithoutException()
        {
            double result = KpiService.CalculateOverdueRate(0, 0);
            Assert.Equal(0.0, result);
        }

        // ===== Тесты Reopen Rate =====

        [Fact]
        public void CalculateReopenRate_NoReopens_Returns0()
        {
            double result = KpiService.CalculateReopenRate(0, 50);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void CalculateReopenRate_TenPercentReopened_Returns10()
        {
            double result = KpiService.CalculateReopenRate(5, 50);
            Assert.Equal(10.0, result);
        }

        [Fact]
        public void CalculateReopenRate_ZeroTotal_ReturnsZeroWithoutException()
        {
            double result = KpiService.CalculateReopenRate(0, 0);
            Assert.Equal(0.0, result);
        }

        // ===== Тесты SafeAverage =====

        [Fact]
        public void SafeAverage_NormalList_ReturnsAverage()
        {
            double result = KpiService.SafeAverage(new[] { 10.0, 20.0, 30.0 });
            Assert.Equal(20.0, result);
        }

        [Fact]
        public void SafeAverage_EmptyList_ReturnsZero()
        {
            double result = KpiService.SafeAverage(System.Array.Empty<double>());
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void SafeAverage_NullInput_ReturnsZero()
        {
            double result = KpiService.SafeAverage(null!);
            Assert.Equal(0.0, result);
        }

        [Fact]
        public void SafeAverage_SingleValue_ReturnsValue()
        {
            double result = KpiService.SafeAverage(new[] { 42.5 });
            Assert.Equal(42.5, result);
        }
    }
}
