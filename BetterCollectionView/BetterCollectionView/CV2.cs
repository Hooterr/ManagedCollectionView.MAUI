using System.Collections;
using System.Diagnostics;

namespace BetterCollectionView;

public class CV2 : ScrollView
{
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(CV2),
        defaultValue: null,
        propertyChanged: (bindable, oldValue, newValue) => ((CV2)bindable).ItemTemplatePropertyChanged((DataTemplate?)oldValue, (DataTemplate?)newValue));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }
    
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(CV2),
        defaultValue: null,
        propertyChanged: (bindable, oldValue, newValue) => ((CV2)bindable).ItemsSourcePropertyChanged((IEnumerable?)oldValue, (IEnumerable?)newValue));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private const int Overscan = 3;
    private List<object>? _itemsSource;
    private readonly List<View> _cache = new();
    private readonly Layout _content;
    private int _firstVisibleItemIdx;

    public CV2()
    {
        _content = new Grid();
        Content = _content;
        Scrolled += OnScrolled;
    }

    private double _previousScrollY;
    
    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        var deltaY = e.ScrollY - _previousScrollY;
        ProcessScroll(deltaY, e.ScrollY);
        _previousScrollY = e.ScrollY;
    }

    private void ProcessScroll(double deltaY, double scrollY)
    {
        Debug.WriteLine($"ScrollY: {scrollY}");
        
        // scrollY is always > 0

        /*foreach (var view in _cache)
        {
            view.TranslationY += deltaY;
        }*/

        bool keepShifting = true;

        while (keepShifting)
        {
            keepShifting = false;
            // Scrolling down 
            if (deltaY > 0)
            {
                var firstVisible = GetViewAt(_firstVisibleItemIdx);
                // 105 - 0 - 100 
                if (scrollY - firstVisible.TranslationY - firstVisible.Height - 8 > 0) // +8 for safety
                {
                    var newItemIdx = _firstVisibleItemIdx + _cache.Count;
                    if (newItemIdx < _itemsSource.Count)
                    {
                        var lastVisible = GetViewAt(_firstVisibleItemIdx + _cache.Count - 1);
                        firstVisible.TranslationY = lastVisible.TranslationY + lastVisible.Height +
                                                    lastVisible.Margin
                                                        .VerticalThickness; // View.Height doesn't include the margin
                        firstVisible.BindingContext = _itemsSource[newItemIdx];
                        _firstVisibleItemIdx++;
                        keepShifting = true;
                    }
                }
            }
            // Scrolling up
            else
            {
                var lastVisible = GetViewAt(_firstVisibleItemIdx + _cache.Count - 1);
                if (lastVisible.TranslationY > scrollY + Height + 8) // +8 for safety
                {
                    var newItemIdx = _firstVisibleItemIdx - 1;
                    if (newItemIdx >= 0)
                    {
                        var firstVisible = GetViewAt(_firstVisibleItemIdx);
                        lastVisible.TranslationY = firstVisible.TranslationY -
                                                   (lastVisible.Height + lastVisible.Margin.VerticalThickness);
                        lastVisible.BindingContext = _itemsSource[newItemIdx];
                        _firstVisibleItemIdx--;
                        keepShifting = true;
                    }
                }
            }
        }
    }
    
    private View GetViewAt(int pos)
    {
        return _cache[pos % _cache.Count];
    }

    private void ItemsSourcePropertyChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (newValue is IList list)
        {
            _itemsSource = new List<object>();
            foreach (var @object in list)
            {
                _itemsSource.Add(@object);
            }
        }
        else if (newValue is IEnumerable<object> objects)
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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        CreateItems();
    }

    private void CreateItems()
    {
        var template = ItemTemplate;
        if (template is null || Height < 0 || _itemsSource is null)
        {
            return;
        }

        var keepAdding = true;
        var currentHeight = 0d;
        var overscanCount = 0;
        var i = 0;
        
        while (i < _itemsSource.Count && keepAdding)
        {
            if (currentHeight >= Height)
            {
                overscanCount++;
                if (overscanCount == Overscan)
                {
                    keepAdding = false;
                }
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
                _content.Add(content);
                _cache.Add(content);
                var size = content.Measure(Width, double.PositiveInfinity, MeasureFlags.IncludeMargins);
                content.TranslationY = currentHeight;
                currentHeight += size.Request.Height;
            }
            
            i++;
        }
        
        Dispatcher.Dispatch(() => _content.HeightRequest = i == 0 ? 0 : _itemsSource.Count * (currentHeight / i));
    }
}