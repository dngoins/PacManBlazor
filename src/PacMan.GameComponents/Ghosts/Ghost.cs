﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Threading.Tasks;
using MediatR;
using PacMan.GameComponents.Canvas;
using PacMan.GameComponents.Events;
using static PacMan.GameComponents.Directions;

namespace PacMan.GameComponents.Ghosts
{
    public abstract class Ghost : SimpleGhost, IGhost
    {
        protected int HouseOffset;
        GhostMover? _mover;

        readonly IGameStats _gameStats;
        readonly IMediator _mediator;
        readonly IHumanInterfaceParser _input;
        readonly IMaze _maze;
        readonly IPacMan _pacman;
        readonly Vector2 _startingPoint;
        readonly Directions _startingDirection;

        Action? _whenInCenterOfNextTile;

        bool _isMoving;

        public abstract Color GetColor();

        public abstract ValueTask<CellIndex> GetScatterTarget();

        public abstract ValueTask<CellIndex> GetChaseTarget();

        protected Ghost(
            IGameStats gameStats,
            IMediator mediator,
            IHumanInterfaceParser input,
            IPacMan pacman,
            GhostNickname nickName,
            IMaze maze,
            Vector2 startingPoint,
            Directions startingDirection) : base(nickName, startingDirection)
        {
            _gameStats = gameStats;
            _mediator = mediator;
            _input = input;
            _maze = maze;
            _pacman = pacman;
            _startingPoint = startingPoint;
            _startingDirection = startingDirection;
            Tile = new Tile();
        }

        public override void PowerPillEaten(GhostFrightSession session)
        {
            base.PowerPillEaten(session);

            if (State == GhostState.Eyes)
            {
                return;
            }

            State = GhostState.Frightened;

            if (MovementMode == GhostMovementMode.Chase || MovementMode == GhostMovementMode.Scatter)
            {
                _whenInCenterOfNextTile = () =>
                {
                    DirectionInfo current = Direction;
                    var c = switchDirectionForChaseOrScatter(current.Current);
                    
                    if (c != None)
                    {
                        Direction.Update(Down);
                    }

                    _mover = new GhostFrightenedMover(this, _maze);
                };
            }
        }

        static Directions switchDirectionForChaseOrScatter(Directions current) =>
            current switch
            {
                Up => Down,
                Down => Up,
                Left => Right,
                Right => Left,
                _ => None
            };

        protected PlayerStats CurrentPlayerStats => _gameStats.CurrentPlayerStats;

        public void SetMovementMode(GhostMovementMode mode) => MovementMode = mode;

        public virtual void Reset()
        {
            Visible = true;

            _isMoving = true;

            State = GhostState.Normal;

            MovementMode = GhostMovementMode.InHouse;

            _whenInCenterOfNextTile = () => { };

            Tile.UpdateWithSpritePos(Tile.ToCenterCanvas(_startingPoint));

            // ReSharper disable once HeapView.ObjectAllocation.Evident
            Direction = new DirectionInfo(_startingDirection, _startingDirection);

            Position = Tile.CenterPos;

            SpriteSheetPos = SpritesheetInfoNormal.GetSourcePosition(Direction.Next, true);
        }

        public int OffsetInHouse => HouseOffset;

        void recenterInLane()
        {
            if (!(MovementMode == GhostMovementMode.Chase || MovementMode == GhostMovementMode.Scatter))
            {
                return;
            }

            var tileCenter = Tile.CenterPos;

            var speed = getSpeed();
            var currentDirection = Direction.Current;

            if (currentDirection == Down || currentDirection == Up)
            {
                var wayToMove = new Vector2(speed, 0);

                if (Position.X > tileCenter.X)
                {
                    Position = Position - wayToMove;
                    Position = new Vector2(Math.Max(Position.X, tileCenter.X), Position.Y);
                }
                else if (Position.X < tileCenter.X)
                {
                    Position = Position + wayToMove;
                    Position = new Vector2(Math.Min(Position.X, tileCenter.X), Position.Y);
                }
            }

            if (currentDirection == Left || currentDirection == Right)
            {
                var wayToMove = new Vector2(0, speed);

                if (Position.Y > tileCenter.Y)
                {
                    Position = Position - wayToMove;
                    Position = new Vector2(Position.X, Math.Max(Position.Y, tileCenter.Y));
                }
                else if (Position.Y < tileCenter.Y)
                {
                    Position = Position + wayToMove;
                    Position = new Vector2(Position.X, Math.Min(Position.Y, tileCenter.Y));
                }
            }
        }

        public override Vector2 Position
        {
            get => Tile.SpritePos;
            set
            {
                var diffAsPoint = value - Position;

                var diff = diffAsPoint;

                if (diff == Vector2.Zero)
                {
                    return;
                }

                Tile.UpdateWithSpritePos(value);
            }
        }

        float getSpeed()
        {
            if (MovementMode == GhostMovementMode.InHouse)
            {
                return .25f;
            }

            if (State == GhostState.Eyes)
            {
                return 2;
            }

            var levelProps = CurrentPlayerStats.LevelStats.GetLevelProps();

            var baseSpeed = Constants.GhostBaseSpeed;

            if (State == GhostState.Frightened)
            {
                return baseSpeed * (levelProps.FrightGhostSpeedPc / 100);
            }

            if (Maze.IsInTunnel(Tile.Index))
            {
                return baseSpeed * (levelProps.GhostTunnelSpeedPc / 100);
            }

            return baseSpeed * (GetNormalGhostSpeedPercent() / 100);
        }

        // virtual (Blinky has different speeds depending on how many dots are left)
        protected virtual float GetNormalGhostSpeedPercent() =>
            CurrentPlayerStats.LevelStats.GetLevelProps().GhostSpeedPc;

        public Tile Tile { get; }

        public void MoveForwards()
        {
            var v = DirectionToIndexLookup.IndexVectorFor(Direction.Current) * getSpeed();
            
            Position += v;
        }

        // ReSharper disable once UnusedMember.Global
        public void StopMoving()
        {
            _isMoving = false;
        }

        public override async ValueTask Update(CanvasTimingInformation timing)
        {
            await base.Update(timing);

            if (!_isMoving)
            {
                return;
            }

            recenterInLane();
            await collisionDetection();

            if (Tile.IsInCenter)
            {
                if (_whenInCenterOfNextTile == null)
                {
                    throw new InvalidOperationException("no action for when in centre of next tile");
                }
                _whenInCenterOfNextTile();
                _whenInCenterOfNextTile = () => { };
            }

            setMoverAndMode();

            await getMover().Update(timing);

            if (State == GhostState.Frightened)
            {
                var frightSession = CurrentPlayerStats.FrightSession;
                if (frightSession == null)
                {
                    throw new InvalidOperationException("no fright session");
                }

                if (frightSession.IsFinished)
                {
                    State = GhostState.Normal;
                }
            }
        }

        public override async ValueTask Draw(CanvasWrapper session)
        {
            await base.Draw(session);

            if (DiagInfo.ShouldShow)
            {
                if (_mover != null)
                {
                    //var targetPoint = ((await GetChaseTarget()).ToVector2() * Vector2s.Eight);
                    var targetPoint = _mover.TargetCell.ToVector2() * Vector2s.Eight;

                    await session.SetGlobalAlphaAsync(.25f);

                    GeneralSprite s = new GeneralSprite(targetPoint, Size, Origin, SpriteSheetPos);

                    await drawLine(Position , targetPoint, session);

                    await session.DrawSprite(s,Spritesheet.Reference);

                }
            }

            await session.SetGlobalAlphaAsync(1f);

            
//            await session.DrawText("X", ((await GetChaseTarget()).ToVector2() * Vector2s.Eight).ToPoint(), Spritesheet.Reference);
        }

        async Task drawLine(Vector2 cellIndex, Vector2 moverTargetCell, CanvasWrapper session)
        {
            await session.DrawLine(cellIndex, moverTargetCell, GetColor());

        }

        void setNextScatterOrChaseMoverAndMode()
        {
            var nextMode = CurrentPlayerStats.ghostMoveConductor.CurrentMode;

            if (MovementMode == nextMode)
            {
                return;
            }

            MovementMode = nextMode;

            if (MovementMode == GhostMovementMode.Scatter)
            {
                _mover = new GhostScatterMover(this, _maze);
                return;
            }

            if (MovementMode == GhostMovementMode.Chase)
            {
                _mover = new GhostChaseMover(this, _maze);
                return;
            }

            throw new InvalidOperationException("Don't know what mover to create!");

        }

        [SuppressMessage("ReSharper", "HeapView.ObjectAllocation.Evident")]
        void setMoverAndMode()
        {
            var isScatterOrChase = MovementMode == GhostMovementMode.Undecided
                                   || MovementMode == GhostMovementMode.Chase
                                   || MovementMode == GhostMovementMode.Scatter;


            if (isScatterOrChase)
            {
                setNextScatterOrChaseMoverAndMode();
                return;
            }

            if (MovementMode == getMover().MovementMode)
            {
                return;
            }

            //sets ghost movement mode to unknown at end
            if (MovementMode == GhostMovementMode.InHouse)
            {
                State = GhostState.Normal;

                _mover = new GhostInsideHouseMover(this, _maze, CurrentPlayerStats.ghostHouseDoor);

                return;
            }

            if (MovementMode == GhostMovementMode.GoingToHouse)
            {
                _mover = new GhostEyesBackToHouseMover(this, _maze, _mediator);
                return;
            }

            //sets ghost movement mode to unknown at end
            if (MovementMode == GhostMovementMode.Frightened)
            {
                _mover = new GhostFrightenedMover(this, _maze);
                return;
            }

            throw new InvalidOperationException("Don't know what mover to create and set!");
        }

        async ValueTask collisionDetection()
        {
            if (Tile.Index != _pacman.Tile.Index)
            {
                return;
            }

            if (State == GhostState.Normal)
            {
                //cheat:
                if (!(Cheats.AllowDebugKeys && _input.IsKeyCurrentlyDown(Keys.Five)))
                {
                    await _mediator.Publish(new PacManEatenEvent());
                }

                return;
            }

            if (State == GhostState.Frightened)
            {
                await _mediator.Publish(new GhostEatenEvent(this));

                State = GhostState.Eyes;
                MovementMode = GhostMovementMode.GoingToHouse;
            }
        }

        protected void SetMover(GhostInsideHouseMover mover) => _mover = mover;

        // ReSharper disable once HeapView.ObjectAllocation.Evident
        GhostMover getMover() => _mover ?? throw new InvalidOperationException("no mover");
    }
}
