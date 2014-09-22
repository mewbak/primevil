﻿using System;

namespace Primevil.Game
{
    public struct Coord
    {
        public int X;
        public int Y;

        public Coord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public CoordF ToCoordF()
        {
            return new CoordF(X, Y);
        }

        public static bool operator ==(Coord a, Coord b)
        {
            return (a.X == b.X) && (a.X == b.X);
        }

        public static bool operator !=(Coord a, Coord b)
        {
            return (a.X != b.X) && (a.X != b.X);
        }

        public static Coord operator +(Coord a, Coord b)
        {
            return new Coord(a.X + b.X, a.Y + b.Y);
        }
    }

    public struct CoordF
    {
        public float X;
        public float Y;

        public CoordF(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Coord ToCoord()
        {
            return new Coord((int)X, (int)Y);
        }

        public float Length
        {
            get
            {
                var x = (double)X;
                var y = (double)Y;
                return (float)Math.Sqrt(x * x + y * y);
            }
        }

        public void Normalize()
        {
            float f = 1.0f / Length;
            X *= f;
            Y *= f;
        }

        public static CoordF operator +(CoordF a, CoordF b)
        {
            return new CoordF(a.X + b.X, a.Y + b.Y);
        }

        public static CoordF operator -(CoordF a, CoordF b)
        {
            return new CoordF(a.X - b.X, a.Y - b.Y);
        }

        public static CoordF operator *(CoordF a, float f)
        {
            return new CoordF(a.X * f, a.Y * f);
        }
    }
}