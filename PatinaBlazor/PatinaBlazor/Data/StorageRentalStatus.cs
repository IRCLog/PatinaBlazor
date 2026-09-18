namespace PatinaBlazor.Data
{
    public enum StorageRentalStatus
    {
        // A unit has been reserved for this rental but no successful charge has happened
        // yet - either the customer hasn't finished the wizard's payment step, or their
        // first charge attempt failed. The unit stays Reserved, not Occupied, until this
        // rental transitions to Active.
        PendingPayment,

        Active,

        // The nightly billing job's charge attempt failed. The unit stays Occupied (the
        // customer isn't evicted over one failed charge) - retried automatically the next
        // night; repeated failures are surfaced on the admin Storage dashboard rather than
        // an automated dunning/cancellation flow.
        PaymentIssue,

        Ended
    }
}
