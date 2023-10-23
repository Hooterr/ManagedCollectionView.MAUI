using System.Collections;
using System.Diagnostics;
using View = Microsoft.Maui.Controls.View;
#if ANDROID
using Android.Views;
#endif

namespace ManagedCollectionView;

public class ManagedVelocityTracker
{
    private const int NumPast = 10;

    private readonly double[] _pastY = new double[NumPast];
    private readonly long[] _pastTime = new long[NumPast];
    private int _lastYIdx;
    
    private readonly Stopwatch _stopwatch = new();
    
    public void Reset()
    {
        _lastYIdx = 0;
        _stopwatch.Reset();
    }

    public void ProcessNextY(double y)
    {
        _pastY[_lastYIdx % 10] = y;
        _pastTime[_lastYIdx % 10] = _stopwatch.ElapsedMilliseconds;
        _lastYIdx++;
    }
    
    public double ComputeVelocityY()
    {
        if (_lastYIdx < 2)
        {
            return 0d;
        }
        
        return 0d;
    }
}

public class ManagedCollectionViewControl : Grid
#if ANDROID

#endif
{
    public ManagedCollectionViewControl()
    {
        var view = new ContentView();
        view.ZIndex = int.MaxValue;
        Add(view);
        
        var pgr = new PanGestureRecognizer();
        pgr.PanUpdated += PgrOnPanUpdated;
        view.GestureRecognizers.Add(pgr);
    }
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(ManagedCollectionViewControl),
        defaultValue: null,
        propertyChanged: (bindable, oldValue, newValue) => ((ManagedCollectionViewControl)bindable).ItemTemplatePropertyChanged((DataTemplate?)oldValue, (DataTemplate?)newValue));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }
    
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(ManagedCollectionViewControl),
        defaultValue: null,
        propertyChanged: (bindable, oldValue, newValue) => ((ManagedCollectionViewControl)bindable).ItemsSourcePropertyChanged((IEnumerable?)oldValue, (IEnumerable?)newValue));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private double _previousPanY;
    private readonly List<View> _cache = new();
    private double _scrollY;
    private int _firstVisibleItemIdx;
    private List<object> _itemsSource = new();
    private double _lastVelocity;
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastMs;
    
    private void ItemsSourcePropertyChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (newValue is IEnumerable<object> objects)
        {
            _itemsSource = new List<object>(objects);
        }
        else
        {
            _itemsSource = new List<object>();
        }
        
        CreateItems();
    }
    
    private void ItemTemplatePropertyChanged(DataTemplate? oldValue, DataTemplate? newValue)
    {
        CreateItems();
    }

#if ANDROID
    private VelocityTracker? _velocityTracker;
    
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is Android.Views.View view)
        {
            view.Touch += OnTouch;
        }
    }

    private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        if (e.Event is null)
        {
            return;
        }

        switch (e.Event.Action)
        {
            case MotionEventActions.Down:
                if (_velocityTracker is null)
                {
                    _velocityTracker = VelocityTracker.Obtain();
                }
                else
                {
                    _velocityTracker.Clear();
                }

                _velocityTracker!.AddMovement(e.Event);
                
                break;
            
            case MotionEventActions.Move:
                _velocityTracker!.AddMovement(e.Event);
                _velocityTracker!.ComputeCurrentVelocity(1000);
                _lastVelocity = _velocityTracker.GetYVelocity(e.Event.GetPointerId(e.Event.ActionIndex));
                break;
            
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _velocityTracker!.Recycle();
                break;
        }

        e.Handled = true;
    }
#endif
    
    private void PgrOnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType is GestureStatus.Started)
        {
            _lastMs = _stopwatch.ElapsedMilliseconds;
            this.AbortAnimation("Scroll");
        }
        
        if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _previousPanY = 0;
            if (double.IsFinite(_lastVelocity))
            {
                new Animation(ProcessScroll, _lastVelocity, 0d, Easing.CubicOut).Commit(this, "Scroll",
                    length: 1000);
            }

            return;
        }

        if (e.StatusType != GestureStatus.Running)
        {
            return;
        }

        var deltaT = _stopwatch.ElapsedMilliseconds - _lastMs;
        var deltaY = e.TotalY - _previousPanY;

        if (deltaT > 250)
        {
            _lastVelocity = 0;
        }
        else
        {
            _lastVelocity = deltaY / deltaT;
        }
        
        _scrollY += deltaY;
        
        Debug.WriteLine($"deltaY: {deltaY}, totalY: {e.TotalY}, scrollY: {_scrollY}");
        
        ProcessScroll(deltaY);

        _previousPanY = e.TotalY;
        _lastMs = _stopwatch.ElapsedMilliseconds;
    }

    private void ProcessScroll(double deltaY)
    {
        _scrollY += deltaY;
        
        foreach (var view in _cache)
        {
            view.TranslationY += deltaY;
        }

        // If moved off screen
        // Scrolling down 
        if (deltaY < 0)
        {
            var firstVisible = GetViewAt(_firstVisibleItemIdx);
            if (-firstVisible.TranslationY > firstVisible.Height + 8) // +8 for safety
            {
                var newItemIdx = _firstVisibleItemIdx + _cache.Count - 1;
                if (newItemIdx < _itemsSource.Count)
                {
                    var lastVisible = GetViewAt(_firstVisibleItemIdx + _cache.Count - 1);
                    firstVisible.TranslationY = lastVisible.TranslationY + lastVisible.Height +
                                                lastVisible.Margin
                                                    .VerticalThickness; // View.Height doesn't include the margin
                    firstVisible.BindingContext = _itemsSource[newItemIdx];
                    _firstVisibleItemIdx++;
                }
            }
        }
        // Scrolling up
        else
        {
            var lastVisible = GetViewAt(_firstVisibleItemIdx + _cache.Count - 1);
            if (lastVisible.TranslationY > Height + 8) // +8 for safety
            {
                var newItemIdx = _firstVisibleItemIdx - 1;
                if (newItemIdx >= 0)
                {
                    var firstVisible = GetViewAt(_firstVisibleItemIdx);
                    lastVisible.TranslationY = firstVisible.TranslationY - (lastVisible.Height + lastVisible.Margin.VerticalThickness);
                    lastVisible.BindingContext = _itemsSource[newItemIdx];
                    _firstVisibleItemIdx--;
                }
            }
        }
    }

    private View GetViewAt(int pos)
    {
        return _cache[pos % _cache.Count];
    }

    private void CreateItems()
    {
        var template = ItemTemplate;
        if (template is null || Height < 0)
        {
            return;
        }

        var keepAdding = true;
        var currentHeight = 0d;
        var i = 0;
        
        while (keepAdding && i < _itemsSource.Count)
        {
            if (currentHeight >= Height && keepAdding)
            {
                keepAdding = false;
            }
            
            View content;
            if (i < _cache.Count)
            {
                content = _cache[i];
                content.BindingContext = _itemsSource[i];
                currentHeight += content.Height;
            }
            else
            {
                content = (View)template.CreateContent();
                content.VerticalOptions = LayoutOptions.Start;
                content.BindingContext = _itemsSource[i];
                Add(content);
                _cache.Add(content);
                var size = content.Measure(Width, double.PositiveInfinity, MeasureFlags.IncludeMargins);
                content.TranslationY = currentHeight;
                currentHeight += size.Request.Height;
            }
            
            i++;
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        CreateItems();
    }
}