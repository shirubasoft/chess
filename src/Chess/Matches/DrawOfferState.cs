namespace Chess;

public union DrawOfferState(NoDrawOffer, PendingDrawOffer)
{
    public static NoDrawOffer None { get; } = new();
}

public sealed class NoDrawOffer
{
    internal NoDrawOffer()
    {
    }
}

public sealed record PendingDrawOffer
{
    public required Side Player { get; init; }
}
