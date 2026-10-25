//
//  RealtimeModelRegistry.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Collections.Generic;

namespace Xmax.SDK
{

    /// <summary>
    /// Registers each built-in server model and its immutable capabilities once.
    /// </summary>
    internal static class RealtimeModelRegistry
    {

        /// <summary>
        /// The model definitions indexed by public model identity.
        /// </summary>
        private static readonly IReadOnlyDictionary<RealtimeModel, ModelDefinition> Definitions =
            new Dictionary<RealtimeModel, ModelDefinition>
            {
                [RealtimeModel.X2_1_1_Preview_CN] = Preview("x2.1.1-preview-cn"),
                [RealtimeModel.X2_1_1_Preview_US] = Preview("x2.1.1-preview-us")
            };

        /// <summary>
        /// Creates the shared sizing capabilities for a regional preview model.
        /// </summary>
        private static ModelDefinition Preview(
            string name
        )
        {
            return new ModelDefinition(
                name,
                new RealtimeModelCapabilities(
                    200000,
                    2100000,
                    32,
                    new RealtimeVideoFormat(
                        1024,
                        1920,
                        30
                    ),
                    new RealtimeVideoSize(
                        1024,
                        1920
                    ),
                    new RealtimeVideoSize(
                        1920,
                        1024
                    )
                )
            );
        }

        /// <summary>
        /// Returns a registered model definition or rejects an unknown model identity.
        /// </summary>
        internal static ModelDefinition Get(
            RealtimeModel model
        )
        {
            if (Definitions.TryGetValue(
                model,
                out var definition
            ))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(model),
                model,
                "Realtime model is not registered."
            );
        }

    }
}
