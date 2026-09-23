namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Application;
using SmartDiskCleaner.Domain;

public sealed class SearchFilterViewModel : ObservableObject
{
    private readonly IScanSearchService _searchService;
    private readonly Action<ScanNode>? _onNodeSelected;
    private ScanSnapshot? _snapshot;

    private string _searchText = "";
    private string _selectedCategory = "All";
    private string _selectedRisk = "All";
    private long _minSizeMb = 0;
    private bool _protectedOnly;
    private bool _reparseOnly;
    private bool _inaccessibleOnly;
    private bool _largeFileOnly;
    private SearchSortOption _selectedSort = SearchSortOption.SizeDescending;
    private ScanNode? _selectedResult;
    private bool _isSearching;
    private string _resultCountText = "0 items found";

    public SearchFilterViewModel(IScanSearchService searchService, Action<ScanNode>? onNodeSelected)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _onNodeSelected = onNodeSelected;

        SearchResults = new ObservableCollection<ScanNode>();
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => _snapshot != null && !_isSearching);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public ObservableCollection<ScanNode> SearchResults { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public string SelectedRisk
    {
        get => _selectedRisk;
        set => SetProperty(ref _selectedRisk, value);
    }

    public long MinSizeMb
    {
        get => _minSizeMb;
        set => SetProperty(ref _minSizeMb, value);
    }

    public bool ProtectedOnly
    {
        get => _protectedOnly;
        set => SetProperty(ref _protectedOnly, value);
    }

    public bool ReparseOnly
    {
        get => _reparseOnly;
        set => SetProperty(ref _reparseOnly, value);
    }

    public bool InaccessibleOnly
    {
        get => _inaccessibleOnly;
        set => SetProperty(ref _inaccessibleOnly, value);
    }

    public bool LargeFileOnly
    {
        get => _largeFileOnly;
        set => SetProperty(ref _largeFileOnly, value);
    }

    public SearchSortOption SelectedSort
    {
        get => _selectedSort;
        set => SetProperty(ref _selectedSort, value);
    }

    public ScanNode? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (SetProperty(ref _selectedResult, value) && value != null)
            {
                _onNodeSelected?.Invoke(value);
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
            {
                ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ResultCountText
    {
        get => _resultCountText;
        set => SetProperty(ref _resultCountText, value);
    }

    public void SetSnapshot(ScanSnapshot? snapshot)
    {
        _snapshot = snapshot;
        SearchResults.Clear();
        ResultCountText = "0 items found";
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
        if (_snapshot != null)
        {
            _ = SearchAsync();
        }
    }

    public async Task SearchAsync()
    {
        if (_snapshot == null) return;

        IsSearching = true;
        try
        {
            CleanupCategory? cat = SelectedCategory switch
            {
                "HighConfidenceReclaimable" => CleanupCategory.HighConfidenceReclaimable,
                "ReviewRecommended" => CleanupCategory.ReviewRecommended,
                "LargeFileNotJunk" => CleanupCategory.LargeFileNotJunk,
                "Protected" => CleanupCategory.Protected,
                "Unknown" => CleanupCategory.Unknown,
                _ => null
            };

            RiskLevel? risk = SelectedRisk switch
            {
                "Low" => RiskLevel.Low,
                "Medium" => RiskLevel.Medium,
                "High" => RiskLevel.High,
                "Protected" => RiskLevel.Protected,
                _ => null
            };

            long? minBytes = MinSizeMb > 0 ? MinSizeMb * 1024 * 1024 : null;

            var criteria = new SearchFilterCriteria(
                SearchText,
                cat,
                risk,
                minBytes,
                ProtectedOnly ? true : null,
                ReparseOnly ? true : null,
                InaccessibleOnly ? true : null,
                LargeFileOnly ? true : null,
                SelectedSort,
                Limit: 1000);

            var snapshot = _snapshot;
            var results = await Task.Run(() => _searchService.Search(snapshot, criteria)).ConfigureAwait(true);

            SearchResults.Clear();
            foreach (var node in results)
            {
                SearchResults.Add(node);
            }
            ResultCountText = $"{results.Count} items found{(results.Count == 1000 ? " (capped at 1,000)" : "")}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void ClearFilters()
    {
        SearchText = "";
        SelectedCategory = "All";
        SelectedRisk = "All";
        MinSizeMb = 0;
        ProtectedOnly = false;
        ReparseOnly = false;
        InaccessibleOnly = false;
        LargeFileOnly = false;
        SelectedSort = SearchSortOption.SizeDescending;
        _ = SearchAsync();
    }
}
