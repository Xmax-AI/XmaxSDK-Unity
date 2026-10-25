//
//  IInteractionController.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System.Collections.Generic;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>Validates and submits normalized media trajectories for the active generation.</summary>
    internal interface IInteractionController
    {

        /// <summary>Binds normalized trajectories to a confirmed generation task.</summary>
        void StartInteraction(
            string taskId
        );

        /// <summary>Stops accepting trajectories and clears the generation task binding.</summary>
        void StopInteraction();

        /// <summary>Submits finite zero-to-one coordinates to the currently bound task.</summary>
        void Send(
            IReadOnlyList<Vector2> tracks
        );
    }
}
