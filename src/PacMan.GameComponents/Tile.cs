﻿using System.Collections.Generic;
using System.Numerics;

namespace PacMan.GameComponents
{
    public class Tile
    {
        // just used for debugging
        // ReSharper disable once NotAccessedField.Local
        bool _isInCenter;

        // ReSharper disable once HeapView.ObjectAllocation.Evident
        readonly Dictionary<Directions, Tile> _nextTiles = new Dictionary<Directions, Tile>();
        
        public Tile()
        {
            UpdateWithSpritePos(Vector2.Zero);
        }

        public Vector2 SpritePos { get; private set; } = Vector2.Zero;

        public void UpdateWithSpritePos(Vector2 spritePos)
        {
            SpritePos = spritePos;
            Index = CellIndex.FromSpritePos(spritePos);

            TopLeft = new Vector2(Index.X * 8, Index.Y * 8);
            CenterPos = TopLeft + Vector2s.Four;
            
            _isInCenter = CenterPos == spritePos.Round();

            handleWrapping();
        }

        public CellIndex Index { get; private set; }
    
        public Vector2 TopLeft { get; private set;}

        /// Get's the canvas center position
        public Vector2 CenterPos { get; private set; } = Vector2.Zero;

        public bool IsInCenter => Vector2s.AreNear(SpritePos, CenterPos, .75);

        public bool IsNearCenter(double precision) => Vector2s.AreNear(SpritePos, CenterPos, precision);

        public Tile NextTile(Directions direction)
        {
            var offset = direction switch
            {
                Directions.Right => new Vector2(1, 0),
                Directions.Left => new Vector2(-1, 0),
                Directions.Up => new Vector2(0, -1),
                Directions.Down => new Vector2(0, 1),
                _ => Vector2.Zero
            };

            Tile tile;

            if (_nextTiles.ContainsKey(direction))
            {
                tile = _nextTiles[direction];
            }
            else
            {
                tile = new Tile();
                _nextTiles.Add(direction, tile);
            }

            tile.UpdateWithSpritePos(CenterPos + offset * Vector2s.Eight);

            return tile;
        }

        public Tile NextTileWrapped(Directions direction)
        {
            var nextTile = NextTile(direction);
            nextTile.handleWrapping();
            return nextTile;
        }

        void handleWrapping()
        {
            float pixelWidthOfMaze = MazeBounds.Dimensions.Width * 8;

            if (Index.X < 0)
            {
                UpdateWithSpritePos(SpritePos + new Vector2(pixelWidthOfMaze, 0));
            }
            else if (Index.X >= 29)
            {
                UpdateWithSpritePos(SpritePos - new Vector2(pixelWidthOfMaze, 0));
            }
        }

        /// <summary>
        /// Given the position of a tile, get the 'canvas position' of it
        /// by multiplying it by 8 and adding 4
        /// </summary>
        /// <param name="tilePos"></param>
        /// <returns></returns>
        public static Vector2 ToCenterCanvas(Vector2 tilePos) => tilePos * Vector2s.Eight + Vector2s.Four;

        /// x & y might not be a round number
        public static Vector2 FromCell(float x, float y)
        {
            Vector2 centerCanvasPosition = Vector2.Multiply(new Vector2(x, y), 8);

            return centerCanvasPosition/Vector2s.Eight;
        }

        public static Tile FromIndex(CellIndex index)
        {
            var tile = new Tile();

            tile.UpdateWithSpritePos(new Vector2(index.X*8, index.Y*8));

            return tile;
        }

// #if DEBUG
//         public override string ToString() => 
//             $"set with ={SpritePos}, in center={_isInCenter} topleft={TopLeft}, index={Index}";
// #endif
    }
}