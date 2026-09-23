namespace SmartDiskCleaner.App.ViewModels;

using System.Collections.ObjectModel;
using SmartDiskCleaner.App.Mvvm;
using SmartDiskCleaner.Domain;

public sealed class TreeNodeViewModel : ObservableObject
{
    private readonly ScanSnapshot _snapshot;
    private readonly Action<ScanNode>? _onSelected;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public TreeNodeViewModel(ScanNode node, ScanSnapshot snapshot, Action<ScanNode>? onSelected)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _onSelected = onSelected;

        Children = new ObservableCollection<TreeNodeViewModel>();

        // Check if node has children in snapshot
        if (node.NodeType == NodeType.Directory)
        {
            HasChildren = _snapshot.ChildrenByParentId.TryGetValue(node.Id, out var children) && children.Count > 0;
        }
    }

    public ScanNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }
    public bool HasChildren { get; }

    public string DisplayName => string.IsNullOrEmpty(Node.Name) ? Node.FullPath : Node.Name;
    public string FullPath => Node.FullPath;
    public NodeType NodeType => Node.NodeType;

    public string DisplaySize
    {
        get
        {
            var bytes = Node.NodeType == NodeType.Directory
                ? (Node.RecursiveUniqueAllocatedBytes ?? Node.RecursiveAllocatedBytes ?? Node.RecursiveLogicalBytes)
                : (Node.UniqueAllocatedBytes ?? Node.AllocatedBytes ?? Node.LogicalBytes);
            return SummaryViewModel.FormatBytes(bytes);
        }
    }

    public string BadgeText
    {
        get
        {
            if (Node.Classification.IsProtected) return "🛡️ [Protected]";
            if (Node.IsReparsePoint) return "🔗 [Reparse]";
            if (!Node.IsAccessible) return "🚫 [Inaccessible]";

            return Node.Classification.Category switch
            {
                CleanupCategory.HighConfidenceReclaimable => "♻️ [Reclaimable]",
                CleanupCategory.ReviewRecommended => "⚠️ [Review]",
                CleanupCategory.LargeFileNotJunk => "📦 [Large File]",
                CleanupCategory.Protected => "🛡️ [Protected]",
                CleanupCategory.Warning => "⚠️ [Warning]",
                _ => ""
            };
        }
    }

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value)
            {
                _onSelected?.Invoke(Node);
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
            {
                EnsureChildrenLoaded();
            }
        }
    }

    public void EnsureChildrenLoaded(int autoExpandMaxDepth = 8, int currentDepth = 0)
    {
        if (_childrenLoaded || !HasChildren) return;
        _childrenLoaded = true;

        if (!_snapshot.ChildrenByParentId.TryGetValue(Node.Id, out var childIds))
        {
            return;
        }

        // Get presentation decisions for this parent if available
        var autoExpandSet = new HashSet<long>();
        if (_snapshot.PresentationDecisions.TryGetValue(Node.Id, out var decision))
        {
            foreach (var id in decision.AutoExpandDirectoryIds)
            {
                autoExpandSet.Add(id);
            }
        }

        foreach (var childId in childIds)
        {
            if (_snapshot.NodeById.TryGetValue(childId, out var childNode))
            {
                var childVm = new TreeNodeViewModel(childNode, _snapshot, _onSelected);
                Children.Add(childVm);

                // Apply AdaptiveTreePolicy automatic expansion decision
                if (autoExpandSet.Contains(childId) && currentDepth < autoExpandMaxDepth)
                {
                    childVm.IsExpanded = true;
                    childVm.EnsureChildrenLoaded(autoExpandMaxDepth, currentDepth + 1);
                }
            }
        }
    }
}
