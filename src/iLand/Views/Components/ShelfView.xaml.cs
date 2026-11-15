using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using iLand.Helpers;
using iLand.ViewModels;

namespace iLand.Views.Components;

/// <summary>
/// Tampilan tab Shelf. Menangani dua hal yang tidak cocok ditaruh di ViewModel:
/// klik dua kali untuk membuka file, dan menyeret tile keluar ke aplikasi lain.
/// (Menjatuhkan file KE island ditangani oleh IslandWindow.)
/// </summary>
public partial class ShelfView : UserControl
{
    private ShelfItemViewModel? _pressedItem;
    private Point _dragStartPoint;

    public ShelfView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Animasi masuk tile BARU (IsEntering): membesar dari 82%, naik dari 8 px di bawah, dan memudar masuk.
    /// Tile yang hanya dimuat ulang (tab dibuka lagi) tidak ditandai IsEntering, jadi tidak dianimasikan.
    /// Beberapa file sekaligus: tiap tile diberi jeda kecil (maksimal untuk 5 tile pertama).
    /// Hanya Opacity yang ditahan sampai selesai (ditimpa trigger IsEntering), lalu dilepas dan IsEntering dikembalikan ke false.
    /// </summary>
    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { DataContext: ShelfItemViewModel { IsEntering: true } item } tile)
        {
            return;
        }

        if (MotionTokens.ReducedMotion
            || tile.RenderTransform is not TransformGroup { Children.Count: >= 2 } group
            || group.Children[0] is not ScaleTransform scale
            || group.Children[1] is not TranslateTransform shift)
        {
            item.IsEntering = false;
            return;
        }

        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(item.EnterIndex, MotionTokens.TileStaggerMaxIndex) * MotionTokens.TileStaggerMs);
        TimeSpan duration = MotionTokens.RowDuration.TimeSpan;
        IEasingFunction ease = MotionTokens.ContentInEase;

        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            tile.BeginAnimation(OpacityProperty, null); // lepas animasi dulu
            item.IsEntering = false;                    // lalu trigger IsEntering ikut lepas
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.82, 1, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.82, 1, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        tile.BeginAnimation(OpacityProperty, fade);
    }

    private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = null;

        if (sender is not FrameworkElement { DataContext: ShelfItemViewModel item })
        {
            return;
        }

        // Klik pada tombol hapus bukan awal dari drag.
        if (e.OriginalSource is DependencyObject source && FindParent<ButtonBase>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            // Klik dua kali = buka file (klik satu kali tidak membuka, agar tidak sengaja menjalankan program).
            item.OpenCommand.Execute(null);
            return;
        }

        _pressedItem = item;
        _dragStartPoint = e.GetPosition(null);
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedItem is null || !_pressedItem.Exists)
        {
            return;
        }

        // Mulai drag hanya jika mouse sudah bergeser cukup jauh (bukan getaran saat klik).
        Point position = e.GetPosition(null);
        bool movedEnough =
            Math.Abs(position.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(position.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!movedEnough)
        {
            return;
        }

        string path = _pressedItem.FullPath;
        _pressedItem = null;

        // Hanya "Copy" yang diizinkan, sehingga file asli TIDAK PERNAH dipindah atau dihapus
        // oleh drag ini, dan file tetap tersimpan di Shelf setelahnya.
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = null;
    }

    private static T? FindParent<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
