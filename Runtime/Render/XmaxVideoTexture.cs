//
//  XmaxVideoTexture.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using UnityEngine;

namespace Xmax.SDK
{

    /// <summary>Uploads I420 video into reusable Unity textures on the main thread.</summary>
    ///
    /// <remarks>Your app owns the material and handles UV rotation and YUV conversion. This object owns and releases the textures it creates.</remarks>
    public sealed class XmaxVideoTexture : IDisposable
    {

        /// <summary>The owned luminance texture, or null before the first upload or after release.</summary>
        public Texture2D Y { get; private set; }

        /// <summary>The owned U chroma texture, or null before the first upload or after release.</summary>
        public Texture2D U { get; private set; }

        /// <summary>The owned V chroma texture, or null before the first upload or after release.</summary>
        public Texture2D V { get; private set; }

        /// <summary>The latest uploaded rotation metadata for application display orientation.</summary>
        public XmaxVideoRotation Rotation { get; private set; }

        /// <summary>Reusable tightly packed plane buffers for texture upload.</summary>
        private byte[][] _packed;

        /// <summary>The currently subscribed video track.</summary>
        private RealtimeVideoTrack _track;

        /// <summary>The optional material owned by your app.</summary>
        private Material _material;

        /// <summary>Whether subscriptions and owned textures have been released.</summary>
        private bool _disposed;

        /// <summary>Replaces the track subscription and applies textures to an optional material on the Unity main thread.</summary>
        ///
        /// <remarks>The material must provide _YTex, _UTex, and _VTex shader properties. Passing a null track removes frame delivery.</remarks>
        public void Bind(
            RealtimeVideoTrack track,
            Material material = null
        )
        {
            RequireAlive();
            if (_track != null)
                _track.FrameReceived -= Upload;

            _track = track;
            _material = material;
            if (track != null)
                track.FrameReceived += Upload;

            ApplyMaterial();
        }

        /// <summary>Uploads a valid I420 frame on the Unity main thread, recreating textures when dimensions change.</summary>
        ///
        /// <remarks>Do not modify the source planes or strides during upload. Packed RGBA and BGRA input is not supported by this texture helper.</remarks>
        public void Upload(
            XmaxVideoFrame frame
        )
        {
            RequireAlive();
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            frame.Validate();
            if (frame.PixelFormat != XmaxVideoPixelFormat.I420)
                throw new XmaxException(
                    XmaxErrorCode.NotSupported,
                    "XmaxVideoTexture accepts I420 remote frames."
                );

            if (Y == null || Y.width != frame.Width || Y.height != frame.Height)
            {
                ReleaseTextures();
                Y = Create(
                    frame.Width,
                    frame.Height
                );
                U = Create(
                    frame.Width / 2,
                    frame.Height / 2
                );
                V = Create(
                    frame.Width / 2,
                    frame.Height / 2
                );
                _packed = new[]
                {
                    new byte[frame.Width * frame.Height],
                    new byte[frame.Width * frame.Height / 4],
                    new byte[frame.Width * frame.Height / 4]
                };
                ApplyMaterial();
            }

            Rotation = frame.Rotation;
            UploadPlane(
                Y,
                frame.Planes[0],
                frame.Strides[0],
                _packed[0]
            );
            UploadPlane(
                U,
                frame.Planes[1],
                frame.Strides[1],
                _packed[1]
            );
            UploadPlane(
                V,
                frame.Planes[2],
                frame.Strides[2],
                _packed[2]
            );
        }

        /// <summary>Creates an owned linear R8 texture without mipmaps, using clamped edges and bilinear filtering.</summary>
        private static Texture2D Create(
            int width,
            int height
        ) => new Texture2D(
            width,
            height,
            TextureFormat.R8,
            false,
            true
        )
        {
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear
        };

        /// <summary>Removes row padding and uploads the packed bytes into one texture.</summary>
        private static void UploadPlane(
            Texture2D texture,
            byte[] data,
            int stride,
            byte[] packed
        )
        {
            for (var row = 0; row < texture.height; row++)
                Buffer.BlockCopy(
                    data,
                    row * stride,
                    packed,
                    row * texture.width,
                    texture.width
                );

            texture.LoadRawTextureData(packed);
            texture.Apply(
                false,
                false
            );
        }

        /// <summary>Assigns the current plane textures to the application material.</summary>
        private void ApplyMaterial()
        {
            if (_material == null)
                return;

            _material.SetTexture(
                "_YTex",
                Y
            );
            _material.SetTexture(
                "_UTex",
                U
            );
            _material.SetTexture(
                "_VTex",
                V
            );
        }

        /// <summary>Rejects binding or upload after this object has been disposed.</summary>
        private void RequireAlive()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(XmaxVideoTexture));
        }

        /// <summary>Releases owned plane textures and upload buffers.</summary>
        private void ReleaseTextures()
        {
            Destroy(Y);
            Destroy(U);
            Destroy(V);
            Y = U = V = null;
            _packed = null;
        }

        /// <summary>Destroys a Unity object using the appropriate play-mode or editor operation.</summary>
        private static void Destroy(
            UnityEngine.Object value
        )
        {
            if (value == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>Removes track subscriptions, clears material texture references, and releases owned textures; safe to repeat on the Unity main thread.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            if (_track != null)
                _track.FrameReceived -= Upload;

            _track = null;
            ReleaseTextures();
            ApplyMaterial();
            _material = null;
            _disposed = true;
        }
    }
}
