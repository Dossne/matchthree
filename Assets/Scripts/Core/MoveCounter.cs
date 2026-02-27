namespace MatchThree.Core
{
    public sealed class MoveCounter
    {
        public int MaxMoves { get; }
        public int Remaining { get; private set; }
        public bool CanMakeMove => Remaining > 0;

        public MoveCounter(int maxMoves)
        {
            MaxMoves = maxMoves < 0 ? 0 : maxMoves;
            Remaining = MaxMoves;
        }

        public void Reset()
        {
            Remaining = MaxMoves;
        }

        public void ConsumeIfAccepted(MoveResult result)
        {
            if (result == null || !result.IsAccepted || Remaining <= 0)
            {
                return;
            }

            Remaining--;
        }
    }
}
