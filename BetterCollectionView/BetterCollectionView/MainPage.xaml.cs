namespace BetterCollectionView;

public partial class MainPage
{

    public MainPage()
    {
        InitializeComponent();
        CollectionView.ItemsSource = Enumerable.Range(0, 10_000).ToList();
    }
    
    private readonly ManagedVelocityTracker _velocityTracker = new();

    private void PanGestureRecognizer_OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _velocityTracker.Reset();
        }
        else if (e.StatusType == GestureStatus.Running)
        {
            _velocityTracker.ProcessNextY(e.TotalY);
        }
        else
        {
            Velocity.Text = $"Last velocity Y: {_velocityTracker.ComputeVelocityY():N5}";
        }
    }
}