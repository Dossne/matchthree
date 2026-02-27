namespace MatchThree.Core
{
    public sealed class GameStateController
    {
        private readonly GoalTracker _goalTracker;
        private readonly MoveCounter _moveCounter;

        public GameState State { get; private set; } = GameState.Playing;

        public GameStateController(GoalTracker goalTracker, MoveCounter moveCounter)
        {
            _goalTracker = goalTracker;
            _moveCounter = moveCounter;
        }

        public GameState EvaluateAfterMove()
        {
            if (State != GameState.Playing)
            {
                return State;
            }

            if (_goalTracker.AllComplete)
            {
                State = GameState.Won;
                return State;
            }

            if (_moveCounter.Remaining <= 0)
            {
                State = GameState.Lost;
            }

            return State;
        }
    }
}
