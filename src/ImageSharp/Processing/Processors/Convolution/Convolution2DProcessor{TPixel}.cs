// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing.Processors.Convolution
{
    /// <summary>
    /// Defines a processor that uses two one-dimensional matrices to perform convolution against an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    internal class Convolution2DProcessor<TPixel> : ImageProcessor<TPixel>
        where TPixel : struct, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Convolution2DProcessor{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
        /// <param name="kernelX">The horizontal gradient operator.</param>
        /// <param name="kernelY">The vertical gradient operator.</param>
        /// <param name="preserveAlpha">Whether the convolution filter is applied to alpha as well as the color channels.</param>
        /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
        /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
        public Convolution2DProcessor(
            Configuration configuration,
            in DenseMatrix<float> kernelX,
            in DenseMatrix<float> kernelY,
            bool preserveAlpha,
            Image<TPixel> source,
            Rectangle sourceRectangle)
            : base(configuration, source, sourceRectangle)
        {
            Guard.IsTrue(kernelX.Size.Equals(kernelY.Size), $"{nameof(kernelX)} {nameof(kernelY)}", "Kernel sizes must be the same.");
            this.KernelX = kernelX;
            this.KernelY = kernelY;
            this.PreserveAlpha = preserveAlpha;
        }

        /// <summary>
        /// Gets the horizontal gradient operator.
        /// </summary>
        public DenseMatrix<float> KernelX { get; }

        /// <summary>
        /// Gets the vertical gradient operator.
        /// </summary>
        public DenseMatrix<float> KernelY { get; }

        /// <summary>
        /// Gets a value indicating whether the convolution filter is applied to alpha as well as the color channels.
        /// </summary>
        public bool PreserveAlpha { get; }

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            using Buffer2D<TPixel> targetPixels = this.Configuration.MemoryAllocator.Allocate2D<TPixel>(source.Width, source.Height);

            source.CopyTo(targetPixels);

            var interest = Rectangle.Intersect(this.SourceRectangle, source.Bounds());

            ParallelRowIterator.IterateRows<RowIntervalAction, Vector4>(
                interest,
                this.Configuration,
                new RowIntervalAction(interest, targetPixels, source.PixelBuffer, this.KernelY, this.KernelX, this.Configuration, this.PreserveAlpha));

            Buffer2D<TPixel>.SwapOrCopyContent(source.PixelBuffer, targetPixels);
        }

        /// <summary>
        /// A <see langword="struct"/> implementing the convolution logic for <see cref="Convolution2DProcessor{T}"/>.
        /// </summary>
        private readonly struct RowIntervalAction : IRowIntervalAction<Vector4>
        {
            private readonly Rectangle bounds;
            private readonly Buffer2D<TPixel> targetPixels;
            private readonly Buffer2D<TPixel> sourcePixels;
            private readonly DenseMatrix<float> kernelY;
            private readonly DenseMatrix<float> kernelX;
            private readonly Configuration configuration;
            private readonly bool preserveAlpha;

            [MethodImpl(InliningOptions.ShortMethod)]
            public RowIntervalAction(
                Rectangle bounds,
                Buffer2D<TPixel> targetPixels,
                Buffer2D<TPixel> sourcePixels,
                DenseMatrix<float> kernelY,
                DenseMatrix<float> kernelX,
                Configuration configuration,
                bool preserveAlpha)
            {
                this.bounds = bounds;
                this.targetPixels = targetPixels;
                this.sourcePixels = sourcePixels;
                this.kernelY = kernelY;
                this.kernelX = kernelX;
                this.configuration = configuration;
                this.preserveAlpha = preserveAlpha;
            }

            /// <inheritdoc/>
            [MethodImpl(InliningOptions.ShortMethod)]
            public void Invoke(in RowInterval rows, Memory<Vector4> memory)
            {
                Span<Vector4> vectorSpan = memory.Span;
                int length = vectorSpan.Length;
                ref Vector4 vectorSpanRef = ref MemoryMarshal.GetReference(vectorSpan);

                int maxY = this.bounds.Bottom - 1;
                int maxX = this.bounds.Right - 1;

                for (int y = rows.Min; y < rows.Max; y++)
                {
                    Span<TPixel> targetRowSpan = this.targetPixels.GetRowSpan(y).Slice(this.bounds.X);
                    PixelOperations<TPixel>.Instance.ToVector4(this.configuration, targetRowSpan.Slice(0, length), vectorSpan);

                    if (this.preserveAlpha)
                    {
                        for (int x = 0; x < this.bounds.Width; x++)
                        {
                            DenseMatrixUtils.Convolve2D3(
                                in this.kernelY,
                                in this.kernelX,
                                this.sourcePixels,
                                ref vectorSpanRef,
                                y,
                                x,
                                this.bounds.Y,
                                maxY,
                                this.bounds.X,
                                maxX);
                        }
                    }
                    else
                    {
                        for (int x = 0; x < this.bounds.Width; x++)
                        {
                            DenseMatrixUtils.Convolve2D4(
                                in this.kernelY,
                                in this.kernelX,
                                this.sourcePixels,
                                ref vectorSpanRef,
                                y,
                                x,
                                this.bounds.Y,
                                maxY,
                                this.bounds.X,
                                maxX);
                        }
                    }

                    PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, vectorSpan, targetRowSpan);
                }
            }
        }
    }
}