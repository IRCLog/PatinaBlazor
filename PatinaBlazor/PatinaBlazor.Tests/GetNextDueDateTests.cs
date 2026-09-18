using PatinaBlazor.Data;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Pure logic, no database - covers StorageRental.GetNextDueDate(), the anchor the
    // nightly billing job (Phase 3) uses to decide what's actually still owed, distinct
    // from GetNextBillingDate(asOf) which just answers "what cycle date applies right
    // now" with no memory of what's already been paid.
    public class GetNextDueDateTests
    {
        [Fact]
        public void NeverBilled_ReturnsThePaymentDateItself()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Monthly,
                PaymentDate = new DateTime(2026, 6, 10),
                LastBilledDate = null
            };

            Assert.Equal(new DateTime(2026, 6, 10), rental.GetNextDueDate());
        }

        [Fact]
        public void JustBilledForTheCurrentCycle_DoesNotComeBackAsStillDue()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Monthly,
                PaymentDate = new DateTime(2026, 6, 10),
                LastBilledDate = new DateTime(2026, 6, 10)
            };

            // Must be the *next* cycle, not the one that was just paid.
            Assert.Equal(new DateTime(2026, 7, 10), rental.GetNextDueDate());
        }

        [Fact]
        public void QuarterlyRental_AdvancesByThreeMonthsAfterBilling()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Quarterly,
                PaymentDate = new DateTime(2026, 1, 15),
                LastBilledDate = new DateTime(2026, 1, 15)
            };

            Assert.Equal(new DateTime(2026, 4, 15), rental.GetNextDueDate());
        }

        [Fact]
        public void AnnualJan31Anchor_DoesNotDriftAfterBillingAcrossAShortMonth()
        {
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Annually,
                PaymentDate = new DateTime(2025, 1, 31),
                LastBilledDate = new DateTime(2025, 1, 31)
            };

            // Same anchor-preservation guarantee GetNextBillingDate already has, exercised
            // through GetNextDueDate this time.
            Assert.Equal(new DateTime(2026, 1, 31), rental.GetNextDueDate());
        }

        [Fact]
        public void MissedACycle_StillOwesTheEarliestUnpaidCycleNotTodaysCycle()
        {
            // Billed for January, but the nightly job somehow never ran again until April -
            // the rental owes February's charge first, not a date computed from "today."
            var rental = new StorageRental
            {
                BillingFrequency = BillingFrequency.Monthly,
                PaymentDate = new DateTime(2026, 1, 10),
                LastBilledDate = new DateTime(2026, 1, 10)
            };

            Assert.Equal(new DateTime(2026, 2, 10), rental.GetNextDueDate());
        }
    }
}
