using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Rendering;
using System;

namespace PaintDotNet.Effects.Gpu.Samples;

internal sealed class RippleEffect
    : CustomEffect<RippleEffect.Props, RippleEffect.Impl>
{
    // The CustomEffect itself is used by the consumer, but does not have any logic of its own.
    // The implementation is in the "Impl" class, which is not seen by the consumer.
    public RippleEffect(IDeviceEffectFactory factory)
        : base(factory)
    {
    }

    public sealed class Props
        : CustomEffectProperties
    {
        public Props()
        {
        }

        public EffectInputAccessor Input => CreateInputAccessor(0);

        // Note that there is not currently a way to express minimum/maximum values for custom effect properties.
        // Direct2D does not validate these anyway.

        public EffectPropertyAccessor<float> Size => CreateFloatPropertyAccessor(0);

        public EffectPropertyAccessor<float> Frequency => CreateFloatPropertyAccessor(1);

        public EffectPropertyAccessor<float> Phase => CreateFloatPropertyAccessor(2);

        public EffectPropertyAccessor<float> Amplitude => CreateFloatPropertyAccessor(3);

        public EffectPropertyAccessor<float> Spread => CreateFloatPropertyAccessor(4);

        public EffectPropertyAccessor<Point2Float> Center => CreatePoint2FloatPropertyAccessor(5);
    }

    public sealed class Impl
        : CustomEffectImpl<Props>
    {
        private RippleTransform? transform;

        public Impl()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.transform?.Dispose();
                this.transform = null;
            }

            base.Dispose(disposing);
        }

        protected override void OnInitialize()
        {
            this.transform = new RippleTransform(this.EffectContext);
            this.TransformGraph.SetSingleTransformNode(this.transform);
            base.OnInitialize();
        }

        protected override void OnSetGraph()
        {
            // SetGraph is only called when the number of inputs changes. This never happens as we
            // publish this effect as a single input effect.
            base.OnSetGraph();
        }

        protected override void OnPrepareForRender(ChangeType changeType)
        {
            this.transform!.Size = this.Properties.Size.GetValue();
            this.transform!.Frequency = this.Properties.Frequency.GetValue();
            this.transform!.Phase = this.Properties.Phase.GetValue();
            this.transform!.Amplitude = this.Properties.Amplitude.GetValue();
            this.transform!.Spread = this.Properties.Spread.GetValue();
            this.transform!.Center = this.Properties.Center.GetValue();
            base.OnPrepareForRender(changeType);
        }
    }
}
