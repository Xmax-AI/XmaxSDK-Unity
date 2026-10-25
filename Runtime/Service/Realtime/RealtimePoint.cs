//
//  RealtimePoint.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

namespace Xmax.SDK
{

    /// <summary>
    /// A normalized media point, from top-left (0, 0) to bottom-right (1, 1).
    /// </summary>
    internal readonly struct RealtimePoint
    {

        /// <summary>
        /// The normalized horizontal position within the full media width, in [0, 1].
        /// </summary>
        internal double X { get; }

        /// <summary>
        /// The normalized vertical position within the full media height, in [0, 1].
        /// </summary>
        internal double Y { get; }

        /// <summary>
        /// Creates a normalized media coordinate resolved by the interaction controller or mapper.
        /// </summary>
        internal RealtimePoint(
            double x,
            double y
        )
        {
            X = x;
            Y = y;
        }
    }
}
