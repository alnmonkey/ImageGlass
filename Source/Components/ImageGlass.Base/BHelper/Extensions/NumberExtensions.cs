﻿/*
ImageGlass Project - Image viewer for Windows
Copyright (C) 2010 - 2025 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
namespace ImageGlass.Base;

public static class NumberExtensions
{
    public static PointF ToPointF(this Point p)
    {
        return new PointF(p.X, p.Y);
    }


    public static Point ToPoint(this PointF p)
    {
        return new Point((int)p.X, (int)p.Y);
    }


    public static RectangleF ToRectangleF(this Rectangle rect)
    {
        return new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
    }


    public static Rectangle ToRectangle(this RectangleF rect)
    {
        return new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
    }

}
