namespace BetterCollectionView;

public partial class CV2Page
{
    public CV2Page()
    {
        InitializeComponent();
        cv.ItemsSource = Enumerable.Range(0, 10_000).ToList();
    }
}