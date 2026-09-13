using System.Windows;

namespace MyGestures.Utils;

class GestureDirectionStorage
    {
        private const int GestureDistanceThreshold = 30;
        private const int MaxDirections = 25;
        private List<MoveDirection> directions = new();
        private Point? lastPoint;

        public void Detect(Point currentPoint)
        {
            if (lastPoint == null)
            {
                lastPoint = currentPoint;
                return;
            }

            if (directions.Count > MaxDirections)
            {
                return;
            }
            
            CreateAndAddDirection(currentPoint, lastPoint.Value);
        }
        
        private void CreateAndAddDirection(Point currentPoint, Point testPoint)
        {
            var deltaX = currentPoint.X - testPoint.X;
            var deltaY = currentPoint.Y - testPoint.Y;

            if (Math.Abs(deltaX) < GestureDistanceThreshold && Math.Abs(deltaY) < GestureDistanceThreshold)
            {
                return;
            }
            
            MoveDirection direction;
            if (Math.Abs(deltaX) > Math.Abs(deltaY))
            {
                direction = deltaX > 0 ? MoveDirection.Right : MoveDirection.Left;
            }
            else
            {
                direction = deltaY > 0 ? MoveDirection.Down : MoveDirection.Up;
            }
            
            if (directions.Count > 0 && directions.Last() == direction)
            {
                return;
            }
            
            directions.Add(direction);
            this.lastPoint = currentPoint;
        }
        
        public MoveDirection[] Directions => directions.ToArray();

        public string DirectionsToDisplay
        {
            get
            {
                if (directions.Count == 0)
                {
                    return string.Empty;
                }
                return directions.Select(ConvertToString).Aggregate((current, next) => current + " " + next);
            }
        }

        private string ConvertToString(MoveDirection move)
        {
            return move switch
            {
                MoveDirection.Up => "↑",
                MoveDirection.Down => "↓",
                MoveDirection.Left => "←",
                MoveDirection.Right => "→",
                _ => throw new ArgumentException($"Invalid direction {move}")
            };
        }

        public void Reset()
        {
            lastPoint = null;
            directions.Clear();
        }
    }