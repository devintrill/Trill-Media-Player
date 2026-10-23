using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Trill
{
    public static class UiMotion
    {
        private static readonly Duration MotionDuration = new Duration(TimeSpan.FromMilliseconds(180));
        private static readonly DependencyProperty WidthGenerationProperty = DependencyProperty.RegisterAttached(
            "WidthGeneration", typeof(int), typeof(UiMotion), new PropertyMetadata(0));

        public static void FadeIn(UIElement element)
        {
            if (element == null) throw new ArgumentNullException("element");

            bool wasAnimating = DependencyPropertyHelper.GetValueSource(element, UIElement.OpacityProperty).IsAnimated;
            double from = wasAnimating ? element.Opacity : 0;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.SetCurrentValue(UIElement.OpacityProperty, 1.0);
            if (!SystemParameters.ClientAreaAnimation) return;

            var animation = new DoubleAnimation(from, 1, MotionDuration)
            {
                EasingFunction = CreateEase(),
                FillBehavior = FillBehavior.Stop
            };
            animation.Freeze();
            element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        public static void SetColumnWidth(ColumnDefinition column, double target, Action completed)
        {
            if (column == null) throw new ArgumentNullException("column");
            if (Double.IsNaN(target) || Double.IsInfinity(target) || target < 0)
                throw new ArgumentOutOfRangeException("target", "Column width must be a finite, nonnegative value.");

            GridLength current = column.Width;
            double from = current.IsAbsolute ? current.Value : column.ActualWidth;
            int generation = unchecked((int)column.GetValue(WidthGenerationProperty) + 1);
            column.SetValue(WidthGenerationProperty, generation);
            column.BeginAnimation(ColumnDefinition.WidthProperty, null);
            column.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(target));

            if (!SystemParameters.ClientAreaAnimation || Math.Abs(from - target) < 0.1)
            {
                if (completed != null) completed();
                return;
            }

            var animation = new GridLengthAnimation
            {
                From = from,
                To = target,
                Duration = MotionDuration,
                EasingFunction = CreateEase(),
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += delegate
            {
                // A superseded closing animation must not hide a reopened sidebar.
                if ((int)column.GetValue(WidthGenerationProperty) != generation) return;
                column.BeginAnimation(ColumnDefinition.WidthProperty, null);
                if (completed != null) completed();
            };
            animation.Freeze();
            column.BeginAnimation(ColumnDefinition.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static CubicEase CreateEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }

        private sealed class GridLengthAnimation : AnimationTimeline
        {
            public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
                "From", typeof(double), typeof(GridLengthAnimation), new PropertyMetadata(0.0));
            public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
                "To", typeof(double), typeof(GridLengthAnimation), new PropertyMetadata(0.0));
            public static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
                "EasingFunction", typeof(EasingFunctionBase), typeof(GridLengthAnimation), new PropertyMetadata(null));

            public double From
            {
                get { return (double)GetValue(FromProperty); }
                set { SetValue(FromProperty, value); }
            }

            public double To
            {
                get { return (double)GetValue(ToProperty); }
                set { SetValue(ToProperty, value); }
            }

            public EasingFunctionBase EasingFunction
            {
                get { return (EasingFunctionBase)GetValue(EasingFunctionProperty); }
                set { SetValue(EasingFunctionProperty, value); }
            }

            public override Type TargetPropertyType { get { return typeof(GridLength); } }

            protected override Freezable CreateInstanceCore()
            {
                return new GridLengthAnimation();
            }

            public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
            {
                double progress = animationClock.CurrentProgress.GetValueOrDefault();
                if (EasingFunction != null) progress = EasingFunction.Ease(progress);
                return new GridLength(Math.Max(0, From + (To - From) * progress));
            }
        }
    }
}
