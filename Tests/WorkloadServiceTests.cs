using Xunit;
using КР_Ханников.Core;
using КР_Ханников.Services;

namespace КР_Ханников.Tests
{
    /// <summary>
    /// Unit-тесты для модуля анализа нагрузки операторов (WorkloadService).
    /// Проверяют корректность формулы весового коэффициента заявки
    /// и алгоритма классификации уровня нагрузки.
    /// </summary>
    public class WorkloadServiceTests
    {
        // ===== Тесты формулы весового коэффициента =====

        [Fact]
        public void GetTicketWeight_Low_Returns1()
        {
            int weight = WorkloadService.GetTicketWeight(TicketPriority.Low);
            Assert.Equal(1, weight);
        }

        [Fact]
        public void GetTicketWeight_Normal_Returns2()
        {
            int weight = WorkloadService.GetTicketWeight(TicketPriority.Normal);
            Assert.Equal(2, weight);
        }

        [Fact]
        public void GetTicketWeight_High_Returns3()
        {
            int weight = WorkloadService.GetTicketWeight(TicketPriority.High);
            Assert.Equal(3, weight);
        }

        [Fact]
        public void GetTicketWeight_Critical_Returns4()
        {
            int weight = WorkloadService.GetTicketWeight(TicketPriority.Critical);
            Assert.Equal(4, weight);
        }

        [Theory]
        [InlineData(TicketPriority.Low, 1)]
        [InlineData(TicketPriority.Normal, 2)]
        [InlineData(TicketPriority.High, 3)]
        [InlineData(TicketPriority.Critical, 4)]
        public void GetTicketWeight_AllPriorities_ReturnsExpectedWeight(
            TicketPriority priority, int expected)
        {
            Assert.Equal(expected, WorkloadService.GetTicketWeight(priority));
        }

        [Fact]
        public void GetTicketWeight_CriticalHeavierThanLow()
        {
            int critical = WorkloadService.GetTicketWeight(TicketPriority.Critical);
            int low = WorkloadService.GetTicketWeight(TicketPriority.Low);
            Assert.True(critical > low,
                "Критическая заявка должна весить больше низкоприоритетной");
        }

        // ===== Тесты классификации уровня нагрузки =====

        [Fact]
        public void ClassifyLevel_ZeroLoad_ReturnsNormal()
        {
            var level = WorkloadService.ClassifyLevel(0.0);
            Assert.Equal(WorkloadLevel.Normal, level);
        }

        [Fact]
        public void ClassifyLevel_HalfLoad_ReturnsNormal()
        {
            var level = WorkloadService.ClassifyLevel(0.5);
            Assert.Equal(WorkloadLevel.Normal, level);
        }

        [Fact]
        public void ClassifyLevel_HighLoad_ReturnsHigh()
        {
            var level = WorkloadService.ClassifyLevel(0.85);
            Assert.Equal(WorkloadLevel.High, level);
        }

        [Fact]
        public void ClassifyLevel_FullLoad_ReturnsHigh()
        {
            var level = WorkloadService.ClassifyLevel(1.0);
            Assert.Equal(WorkloadLevel.High, level);
        }

        [Fact]
        public void ClassifyLevel_OverloadOverLimit_ReturnsOverloaded()
        {
            var level = WorkloadService.ClassifyLevel(1.2);
            Assert.Equal(WorkloadLevel.Overloaded, level);
        }
    }
}
