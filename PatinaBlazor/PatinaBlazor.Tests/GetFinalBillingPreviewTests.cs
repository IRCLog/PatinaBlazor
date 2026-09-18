using PatinaBlazor.Data;

namespace PatinaBlazor.Tests
{
    // Covers StorageRental.GetFinalBillingPreview - the "End Rental" admin dialog's live
    // preview of what, if anything, is still owed if the customer moves out on a given date.
    // Pure logic, no database - StorageRental's own anchor-preserving date math (already
    // proven correct by GetNextBillingDateTests/GetNextDueDateTests) is reused directly, so
    // these tests focus on the zero-vs-charged boundary and multi-cycle spans specifically.
    public class GetFinalBillingPreviewTests
    {
        private static StorageRental CreateMonthlyRental(DateTime? lastBilledDate) => new()
        {
            PaymentDate = new DateTime(2026, 1, 15),
            BillingFrequency = BillingFrequency.Monthly,
            MonthlyRateAtSigning = 100m,
            LastBilledDate = lastBilledDate
        };

        [Fact]
        public void MoveOutBeforeNextDueDate_ReturnsZeroAndNoFinalBillingDate()
        {
            var rental = CreateMonthlyRental(new DateTime(2026, 3, 15)); // next due: 2026-04-15

            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 4, 1));

            Assert.Null(finalBillingDate);
            Assert.Equal(0m, finalAmount);
        }

        [Fact]
        public void MoveOutExactlyOnNextDueDate_ChargesExactlyOneMoreCycle()
        {
            var rental = CreateMonthlyRental(new DateTime(2026, 3, 15)); // next due: 2026-04-15

            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 4, 15));

            Assert.Equal(new DateTime(2026, 4, 15), finalBillingDate);
            Assert.Equal(100m, finalAmount);
        }

        [Fact]
        public void MoveOutSeveralCyclesPastNextDueDate_ChargesEveryCycleSpanned()
        {
            var rental = CreateMonthlyRental(new DateTime(2026, 3, 15)); // next due: 2026-04-15

            // Spans 4/15, 5/15, 6/15 - not 7/15, since 2026-06-20 is before it.
            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 6, 20));

            Assert.Equal(new DateTime(2026, 6, 15), finalBillingDate);
            Assert.Equal(300m, finalAmount);
        }

        [Fact]
        public void NeverBilled_MoveOutBeforePaymentDate_ReturnsZero()
        {
            var rental = CreateMonthlyRental(lastBilledDate: null); // next due: PaymentDate itself (2026-01-15)

            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 1, 1));

            Assert.Null(finalBillingDate);
            Assert.Equal(0m, finalAmount);
        }

        [Fact]
        public void NeverBilled_MoveOutOnOrAfterPaymentDate_ChargesTheFirstCycle()
        {
            var rental = CreateMonthlyRental(lastBilledDate: null); // next due: PaymentDate itself (2026-01-15)

            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 1, 15));

            Assert.Equal(new DateTime(2026, 1, 15), finalBillingDate);
            Assert.Equal(100m, finalAmount);
        }

        [Fact]
        public void QuarterlyRental_FinalAmountUsesTheFullQuarterlyChargeNotTheMonthlyRate()
        {
            var rental = new StorageRental
            {
                PaymentDate = new DateTime(2026, 1, 15),
                BillingFrequency = BillingFrequency.Quarterly,
                MonthlyRateAtSigning = 100m,
                LastBilledDate = new DateTime(2026, 1, 15) // next due: 2026-04-15
            };

            var (finalBillingDate, finalAmount) = rental.GetFinalBillingPreview(new DateTime(2026, 4, 15));

            Assert.Equal(new DateTime(2026, 4, 15), finalBillingDate);
            Assert.Equal(300m, finalAmount); // $100/mo * 3 months, not $100
        }
    }
}
