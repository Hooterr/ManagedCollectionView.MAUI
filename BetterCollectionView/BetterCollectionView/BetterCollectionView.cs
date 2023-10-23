using System.Collections;
using System.Diagnostics;
using View = Microsoft.Maui.Controls.View;
#if ANDROID
using Android.Views;
#endif

namespace BetterCollectionView;

public class ManagedVelocityTracker
{
    private const int NumPast = 10;
    private const long MaxAgeMs = 200;

    private readonly double[] _pastY = new double[NumPast];
    private readonly long[] _pastTime = new long[NumPast];
    private int _lastTouchIdx;
    
    private readonly Stopwatch _stopwatch = new();

    public ManagedVelocityTracker()
    {
        _stopwatch.Start();
    }
    
    public void Reset()
    {
        _lastTouchIdx = 0;
        _pastTime[0] = long.MinValue;
    }

    public void ProcessNextY(double y)
    {
        _lastTouchIdx = (_lastTouchIdx + 1) % NumPast;
        _pastY[_lastTouchIdx % NumPast] = y;
        _pastTime[_lastTouchIdx % NumPast] = _stopwatch.ElapsedMilliseconds;
    }
    
    public double ComputeVelocityY(uint units = 1)
    {
        // We only care about the last 200ms of activity
        var oldestTouchIndex = _lastTouchIdx;
        var numberOfTouches = 1;
        var minTime = _pastTime[_lastTouchIdx] - MaxAgeMs;
        while (numberOfTouches < NumPast)
        {
            var nextOldestTouchIndex = (oldestTouchIndex + NumPast - 1) % NumPast;
            var nextHeadTouchTime = _pastTime[nextOldestTouchIndex];
            if (nextHeadTouchTime < minTime)
            {
                break;
            }

            oldestTouchIndex = nextOldestTouchIndex;
            numberOfTouches++;
        }
        
        if (numberOfTouches > 3)
        {
            numberOfTouches--;
        }

        var oldestTime = _pastTime[oldestTouchIndex];
        var oldestY = _pastY[oldestTouchIndex];
        double accumY = 0;
        
        for (int i = 1; i < numberOfTouches; i++)
        {
            var touchIndex = (oldestTouchIndex + i) % NumPast;
            var duration = (int)(_pastTime[touchIndex] - oldestTime);

            if (duration == 0)
            {
                continue;
            }

            var delta = _pastY[touchIndex] - oldestY;
            var velocity = (delta / duration) * units;
            accumY = accumY == 0 ? velocity : (accumY + velocity) * 0.5d;
        }
        
        Debug.WriteLine($"Touch velocity: {accumY} based on {numberOfTouches} touch events");

        
        return accumY;
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
    private Stopwatch _stopwatch = Stopwatch.StartNew();

    private readonly ManagedVelocityTracker _velocityTracker = new(); 
    
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

    private void PgrOnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType is GestureStatus.Started)
        {
            _velocityTracker.Reset();
            this.AbortAnimation("Scroll");
        }
        else if (e.StatusType is GestureStatus.Running)
        {
            _velocityTracker.ProcessNextY(e.TotalY);
            var deltaY = e.TotalY - _previousPanY;

            _scrollY += deltaY;
        
            Debug.WriteLine($"deltaY: {deltaY}, totalY: {e.TotalY}, scrollY: {_scrollY}");
        
            ProcessScroll(deltaY);

            _previousPanY = e.TotalY;
        }
        else
        {
            _previousPanY = 0;
            var velocity = _velocityTracker.ComputeVelocityY();
            Debug.WriteLine($"Fling with velocity: {velocity:N5}");
            new Animation(ProcessScroll, velocity, 0d, Easing.CubicOut).Commit(this, "Scroll", length: 1000);
        }
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