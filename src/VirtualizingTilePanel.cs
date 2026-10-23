using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Trill
{
    // Fixed-size wrapping tiles with pixel scrolling. Only visible rows and a
    // small buffer have visual containers, regardless of the folder item count.
    public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty FixedColumnsProperty = DependencyProperty.Register(
            "FixedColumns", typeof(int), typeof(VirtualizingTilePanel),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
            "ItemHeight", typeof(double), typeof(VirtualizingTilePanel),
            new FrameworkPropertyMetadata(178.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
        public int FixedColumns { get { return (int)GetValue(FixedColumnsProperty); } set { SetValue(FixedColumnsProperty, value); } }
        public double ItemHeight { get { return (double)GetValue(ItemHeightProperty); } set { SetValue(ItemHeightProperty, value); } }
        private double CellWidth = 192;
        private double CellHeight { get { return Math.Max(1, ItemHeight); } }
        private int columns = 1;
        private Size extent;
        private Size viewport;
        private double offset;

        protected override Size MeasureOverride(Size available)
        {
            ItemsControl owner = ItemsControl.GetItemsOwner(this);
            if (owner == null) return new Size();
            double width = Double.IsInfinity(available.Width) ? Math.Max(CellWidth, ActualWidth) : available.Width;
            double height = Double.IsInfinity(available.Height) ? Math.Max(CellHeight, ActualHeight) : available.Height;
            columns = FixedColumns > 0 ? FixedColumns : Math.Max(1, (int)(width / 192));
            CellWidth = FixedColumns > 0 ? Math.Max(1, width / columns) : 192;
            viewport = new Size(width, height);
            extent = new Size(width, Math.Ceiling((double)owner.Items.Count / columns) * CellHeight);
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, extent.Height - viewport.Height)));
            if (ScrollOwner != null) ScrollOwner.InvalidateScrollInfo();

            int first = Math.Max(0, ((int)(offset / CellHeight) - 1) * columns);
            int last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((offset + height) / CellHeight) + 1) * columns - 1);
            int realizedCount = InternalChildren.Count; // Initializes the item-container generator.
            IItemContainerGenerator generator = ItemContainerGenerator;
            for (int i = realizedCount - 1; i >= 0; i--)
            {
                GeneratorPosition position = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(position);
                if (itemIndex < first || itemIndex > last)
                {
                    generator.Remove(position, 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
            if (last >= first)
            {
                GeneratorPosition start = generator.GeneratorPositionFromIndex(first);
                int childIndex = start.Offset == 0 ? start.Index : start.Index + 1;
                using (generator.StartAt(start, GeneratorDirection.Forward, true))
                {
                    for (int index = first; index <= last; index++, childIndex++)
                    {
                        bool added;
                        UIElement child = (UIElement)generator.GenerateNext(out added);
                        if (added)
                        {
                            if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                            else InsertInternalChild(childIndex, child);
                            generator.PrepareItemContainer(child);
                        }
                        child.Measure(new Size(CellWidth, CellHeight));
                    }
                }
            }
            return viewport;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                InternalChildren[i].Arrange(new Rect((index % columns) * CellWidth, (index / columns) * CellHeight - offset, CellWidth, CellHeight));
            }
            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Remove || args.Action == NotifyCollectionChangedAction.Replace || args.Action == NotifyCollectionChangedAction.Move)
            {
                int index = args.Action == NotifyCollectionChangedAction.Move ? args.OldPosition.Index : args.Position.Index;
                if (index >= 0 && index < InternalChildren.Count && args.ItemUICount > 0)
                    RemoveInternalChildRange(index, Math.Min(args.ItemUICount, InternalChildren.Count - index));
            }
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                RemoveInternalChildRange(0, InternalChildren.Count);
                offset = 0;
            }
            InvalidateMeasure();
        }

        protected override void BringIndexIntoView(int index)
        {
            double top = (index / columns) * CellHeight;
            if (top < offset) SetVerticalOffset(top);
            else if (top + CellHeight > offset + viewport.Height) SetVerticalOffset(top + CellHeight - viewport.Height);
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            UIElement child = visual as UIElement;
            while (child != null && VisualTreeHelper.GetParent(child) != this)
                child = VisualTreeHelper.GetParent(child) as UIElement;
            if (child == null) return Rect.Empty;
            int childIndex = InternalChildren.IndexOf(child);
            if (childIndex >= 0) BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0)));
            return rectangle;
        }

        public void SetVerticalOffset(double value)
        {
            if (Double.IsNaN(value)) return;
            double next = Math.Max(0, Math.Min(value, Math.Max(0, extent.Height - viewport.Height)));
            if (Math.Abs(next - offset) < .01) return;
            offset = next;
            if (ScrollOwner != null) ScrollOwner.InvalidateScrollInfo();
            InvalidateMeasure();
        }
        public void LineUp() { SetVerticalOffset(offset - 32); }
        public void LineDown() { SetVerticalOffset(offset + 32); }
        public void PageUp() { SetVerticalOffset(offset - viewport.Height); }
        public void PageDown() { SetVerticalOffset(offset + viewport.Height); }
        public void MouseWheelUp() { SetVerticalOffset(offset - WheelStep); }
        public void MouseWheelDown() { SetVerticalOffset(offset + WheelStep); }
        private double WheelStep { get { return SystemParameters.WheelScrollLines < 0 ? viewport.Height : Math.Max(0, SystemParameters.WheelScrollLines) * 32; } }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double value) { }
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth { get { return extent.Width; } }
        public double ExtentHeight { get { return extent.Height; } }
        public double ViewportWidth { get { return viewport.Width; } }
        public double ViewportHeight { get { return viewport.Height; } }
        public double HorizontalOffset { get { return 0; } }
        public double VerticalOffset { get { return offset; } }
        public ScrollViewer ScrollOwner { get; set; }
    }
}
