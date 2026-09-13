using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Pure logic, no database - the anchor-preserving date math this originally shipped with
    // (see CLAUDE.md's 2026-09-05 billing-frequency entry) was verified once via a throwaway
    // console harness and then deleted; these cases reproduce that verification permanently.
    public class GetNextBillingDateTests
    {
        [Fact]
        public void MonthlyAnchorOnJan31DoesNotDriftAcrossShortMonths()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Monthly,
                PaymentDate = new DateTime(2026, 1, 31)
            };

            Assert.Equal(new DateTime(2026, 2, 28), rental.GetNextBillingDate(new DateTime(2026, 2, 1)));
            Assert.Equal(new DateTime(2026, 3, 31), rental.GetNextBillingDate(new DateTime(2026, 3, 1)));
            Assert.Equal(new DateTime(2026, 4, 30), rental.GetNextBillingDate(new DateTime(2026, 4, 1)));
            Assert.Equal(new DateTime(2026, 5, 31), rental.GetNextBillingDate(new DateTime(2026, 5, 1)));
        }

        [Fact]
        public void QuarterlyAnchorAdvancesByThreeMonths()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Quarterly,
                PaymentDate = new DateTime(2026, 1, 15)
            };

            Assert.Equal(new DateTime(2026, 1, 15), rental.GetNextBillingDate(new DateTime(2026, 1, 15)));
            Assert.Equal(new DateTime(2026, 4, 15), rental.GetNextBillingDate(new DateTime(2026, 2, 1)));
            Assert.Equal(new DateTime(2026, 4, 15), rental.GetNextBillingDate(new DateTime(2026, 4, 15)));
            Assert.Equal(new DateTime(2026, 7, 15), rental.GetNextBillingDate(new DateTime(2026, 4, 16)));
        }

        [Fact]
        public void AnnualFeb29AnchorReturnsToLeapYearInsteadOfDriftingToFeb28()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Annually,
                PaymentDate = new DateTime(2024, 2, 29) // 2024 is a leap year
            };

            // 2025, 2026, 2027 are not leap years - AddMonths(12) from Feb 29 clamps to Feb 28
            Assert.Equal(new DateTime(2025, 2, 28), rental.GetNextBillingDate(new DateTime(2025, 1, 1)));
            Assert.Equal(new DateTime(2026, 2, 28), rental.GetNextBillingDate(new DateTime(2026, 1, 1)));
            // 2028 is a leap year again - must return to Feb 29, not stay clamped at Feb 28
            Assert.Equal(new DateTime(2028, 2, 29), rental.GetNextBillingDate(new DateTime(2028, 1, 1)));
        }

        [Fact]
        public void AsOfExactlyOnPaymentDateReturnsPaymentDateItself()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Monthly,
                PaymentDate = new DateTime(2026, 6, 10)
            };

            Assert.Equal(new DateTime(2026, 6, 10), rental.GetNextBillingDate(new DateTime(2026, 6, 10)));
        }
    }
}
