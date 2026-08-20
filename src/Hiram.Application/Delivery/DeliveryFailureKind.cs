namespace Hiram.Application.Delivery;

// Why a permanent failure happened, when the provider says enough to know. It separates the cases an
// operator has to act on from the ones a tenant has to: a region left out of the account's geo permissions
// and a recipient who replied STOP both end in a dead letter, and the fix for one has nothing to do with
// the other.
public enum DeliveryFailureKind
{
    // The provider refused and said why, and the reason does not fall into any case below.
    Provider = 0,

    // The account or the tenant configuration is wrong. No retry and no other recipient changes it.
    Configuration,

    // The recipient told the carrier to stop. Sending again is both useless and a compliance problem.
    RecipientOptedOut,

    // The address does not exist. The contact is wrong at the source, not in this message.
    InvalidDestination
}
