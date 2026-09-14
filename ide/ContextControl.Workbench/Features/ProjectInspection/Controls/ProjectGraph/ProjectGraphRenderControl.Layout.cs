using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;
public sealed partial class ProjectGraphRenderControl
{
    private void EnsureLayout()
    {
        if (!_layoutDirty)
        {
            return;
        }

        _layoutDirty = false;
        _roots.Clear();
        _nodes.Clear();
        _edges.Clear();
        _edgeRoutes.Clear();
        _edgeRouteSegments.Clear();

        var remaining = MaxGraphNodes;
        var roots = Items;
        if (roots is not null)
        {
            foreach (var root in roots)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var graphRoot = AddGraphNode(null, root, 0, ref remaining);
                if (graphRoot is not null)
                {
                    _roots.Add(graphRoot);
                }
            }
        }

        foreach (var root in _roots)
        {
            MeasureWeight(root);
        }

        foreach (var root in _roots)
        {
            MeasureTreeSize(root);
        }

        if (_roots.Count == 1)
        {
            AssignTreeLayout(_roots[0], new Point(LayoutMargin, LayoutMargin));
        }
        else
        {
            var plan = CreatePackPlan(_roots, node => node.TreeSize, TreeAspect, TreePackGap, TreeModuleGap, forceGrid: false);
            foreach (var item in plan.Items)
            {
                AssignTreeLayout(item.Node, new Point(LayoutMargin + item.Offset.X, LayoutMargin + item.Offset.Y));
            }
        }

        foreach (var node in _nodes)
        {
            if (_manualPositions.TryGetValue(node.Key, out var pinned))
            {
                node.Bounds = new Rect(pinned, node.Bounds.Size);
                node.IsPinned = true;
            }
            else
            {
                node.IsPinned = false;
            }
        }

        _contentBounds = CalculateContentBounds();
        RebuildEdgeRoutes();
    }

    private bool IsCubeLayoutMode =>
        string.Equals(LayoutMode, "cube", StringComparison.OrdinalIgnoreCase);

    private GraphNode? AddGraphNode(GraphNode? parent, ProjectNodeViewModel node, int depth, ref int remaining)
    {
        if (remaining <= 0)
        {
            return null;
        }

        remaining--;
        var graphNode = new GraphNode(node, parent, depth);
        AddGraphNode(parent, graphNode);

        var orderedChildren = new List<ProjectNodeViewModel>(node.Children);
        orderedChildren.Sort(CompareProjectGraphChildren);
        var shownChildren = 0;
        var omittedCount = 0;
        var omittedCapped = false;

        foreach (var child in orderedChildren)
        {
            if (shownChildren >= MaxChildrenPerParent || remaining <= 1)
            {
                var counted = CountSubtreeNodesCapped(child, OmittedCountCap - omittedCount);
                omittedCount += counted.Count;
                omittedCapped |= counted.Capped;
                if (omittedCount >= OmittedCountCap)
                {
                    omittedCount = OmittedCountCap;
                    omittedCapped = true;
                }

                continue;
            }

            if (AddGraphNode(graphNode, child, depth + 1, ref remaining) is not null)
            {
                shownChildren++;
            }
        }

        if (omittedCount > 0 && remaining > 0)
        {
            remaining--;
            AddGraphNode(graphNode, GraphNode.CreateAggregate(graphNode, depth + 1, omittedCount, omittedCapped));
        }

        return graphNode;
    }

    private void AddGraphNode(GraphNode? parent, GraphNode graphNode)
    {
        parent?.Children.Add(graphNode);
        _nodes.Add(graphNode);
        if (parent is not null)
        {
            _edges.Add(new GraphEdge(parent, graphNode));
        }
    }

    private static CountResult CountSubtreeNodesCapped(ProjectNodeViewModel node, int cap)
    {
        if (cap <= 0)
        {
            return new CountResult(0, true);
        }

        var count = 0;
        var stack = new Stack<ProjectNodeViewModel>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            if (count >= cap)
            {
                return new CountResult(cap, true);
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return new CountResult(count, false);
    }

    private static double MeasureWeight(GraphNode node)
    {
        if (node.Children.Count == 0)
        {
            node.Weight = 1.0;
            return node.Weight;
        }

        var weight = 0.0;
        foreach (var child in node.Children)
        {
            weight += MeasureWeight(child);
        }

        node.Weight = Math.Max(1.0, weight);
        return node.Weight;
    }

    private static int CompareProjectGraphChildren(ProjectNodeViewModel left, ProjectNodeViewModel right)
    {
        if (left.IsFolder != right.IsFolder)
        {
            return left.IsFolder ? -1 : 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }

    private Size MeasureTreeSize(GraphNode node)
    {
        var nodeSize = MeasureNodeSize(node);
        if (node.Children.Count == 0)
        {
            node.TreePlan = null;
            node.TreeSize = nodeSize;
            return node.TreeSize;
        }

        foreach (var child in node.Children)
        {
            MeasureTreeSize(child);
        }

        var plan = CreateNodeChildPackPlan(node, child => child.TreeSize);
        node.TreePlan = plan;
        var labelOverlap = nodeSize.Height * 0.5;
        var regionWidth = node.Parent is null
            ? Math.Max(plan.Width, nodeSize.Width + TreeLabelSidePadding * 2.0)
            : Math.Max(
                plan.Width + TreeRegionPadding * 2.0,
                nodeSize.Width + TreeLabelSidePadding * 2.0);
        var regionHeight = node.Parent is null
            ? Math.Max(plan.Height, node.RootLabelOffsetY + nodeSize.Height)
            : labelOverlap + TreeChildGapY + plan.Height + TreeRegionPadding;
        node.TreeSize = new Size(
            regionWidth,
            node.Parent is null ? regionHeight : labelOverlap + regionHeight);
        return node.TreeSize;
    }

    private void AssignTreeLayout(GraphNode node, Point origin)
    {
        var nodeSize = MeasureNodeSize(node);
        if (node.Children.Count == 0)
        {
            node.RegionBounds = default;
            node.Bounds = new Rect(origin, nodeSize);
            return;
        }

        var plan = node.TreePlan ?? CreateNodeChildPackPlan(node, child => child.TreeSize);
        var labelOverlap = nodeSize.Height * 0.5;
        if (node.Parent is null)
        {
            node.RegionBounds = new Rect(origin, node.TreeSize);
            node.Bounds = new Rect(
                origin.X + Math.Max(0, (node.TreeSize.Width - nodeSize.Width) * 0.5),
                origin.Y + node.RootLabelOffsetY,
                nodeSize.Width,
                nodeSize.Height);

            var rootChildX = Math.Max(0, (node.TreeSize.Width - plan.Width) * 0.5);
            foreach (var item in plan.Items)
            {
                AssignTreeLayout(item.Node, new Point(origin.X + rootChildX + item.Offset.X, origin.Y + item.Offset.Y));
            }

            return;
        }

        node.RegionBounds = new Rect(
            origin.X,
            origin.Y + labelOverlap,
            node.TreeSize.Width,
            Math.Max(1.0, node.TreeSize.Height - labelOverlap));
        node.Bounds = new Rect(
            origin.X + Math.Max(0, (node.TreeSize.Width - nodeSize.Width) * 0.5),
            origin.Y,
            nodeSize.Width,
            nodeSize.Height);

        var childOrigin = new Point(
            node.RegionBounds.X + Math.Max(0, (node.RegionBounds.Width - plan.Width) * 0.5),
            node.RegionBounds.Y + labelOverlap + TreeChildGapY);
        foreach (var item in plan.Items)
        {
            AssignTreeLayout(item.Node, new Point(childOrigin.X + item.Offset.X, childOrigin.Y + item.Offset.Y));
        }
    }

    private GraphPackPlan CreatePackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap,
        bool forceGrid)
    {
        if (children.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        if (HasMixedPackKinds(children))
        {
            return CreateMixedPackPlan(children, getSize, aspect, gap, moduleGap);
        }

        return CreateUnmixedPackPlan(children, getSize, aspect, gap, moduleGap, forceGrid);
    }

    private GraphPackPlan CreateNodeChildPackPlan(
        GraphNode node,
        Func<GraphNode, Size> getSize)
    {
        if (node.Parent is null)
        {
            return CreateRootPackPlan(node, getSize);
        }

        return CreateGenerationPackPlan(node.Children, getSize);
    }

    private GraphPackPlan CreateGenerationPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize)
    {
        var modules = children.Where(ContinuesGeneration).ToArray();
        var files = children.Where(child => !ContinuesGeneration(child)).ToArray();

        if (files.Length == 0)
        {
            return CreateCompactCubePackPlan(modules, getSize, CubePackGap, CubePackGap);
        }

        if (modules.Length == 0)
        {
            return CreateCompactCubePackPlan(files, getSize, TreePackGap, TreePackGap);
        }

        var fileCandidates = CreateCompactPackCandidates(files, getSize, TreePackGap, TreePackGap, maxCandidates: 48);
        var moduleCandidates = CreateCompactPackCandidates(modules, getSize, CubePackGap, CubePackGap, maxCandidates: 48);
        GraphPackPlan? best = null;
        var bestPerimeter = double.MaxValue;
        var bestSquarePenalty = double.MaxValue;
        var bestArea = double.MaxValue;

        foreach (var fileCandidate in fileCandidates)
        {
            foreach (var moduleCandidate in moduleCandidates)
            {
                var plan = CreateLayeredPackPlan(fileCandidate, moduleCandidate, GenerationSectionGap, children.Count);
                var perimeter = plan.Width + plan.Height;
                var squarePenalty = MeasureSquarePenalty(plan.Width, plan.Height);
                var area = plan.Width * plan.Height;
                if (IsBetterCompactPlan(perimeter, squarePenalty, area, bestPerimeter, bestSquarePenalty, bestArea))
                {
                    best = plan;
                    bestPerimeter = perimeter;
                    bestSquarePenalty = squarePenalty;
                    bestArea = area;
                }
            }
        }

        return best ?? CreateLayeredPackPlan(fileCandidates[0], moduleCandidates[0], GenerationSectionGap, children.Count);
    }

    private GraphPackPlan CreateRootPackPlan(
        GraphNode root,
        Func<GraphNode, Size> getSize)
    {
        var children = root.Children;
        if (children.Count == 0)
        {
            root.RootLabelOffsetY = 0;
            return new GraphPackPlan([], 0, 0);
        }

        var rootSize = MeasureNodeSize(root);
        var modules = children.Where(IsPackModule).ToArray();
        var files = children.Where(child => !IsPackModule(child)).ToArray();
        var filePlan = files.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateGridPackPlan(files, getSize, TreeAspect, TreePackGap);
        var modulePlan = modules.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : IsCubeLayoutMode
                ? CreateCubeRootModulePackPlan(modules, getSize, filePlan, rootSize)
                : CreateSingleRowPackPlan(modules, getSize, TreeModuleGap);
        root.RootLabelOffsetY = filePlan.Height > 0
            ? filePlan.Height + RootFileBlockGap
            : 0;
        var moduleY = root.RootLabelOffsetY + rootSize.Height + TreeChildGapY;
        var width = Math.Max(Math.Max(modulePlan.Width, filePlan.Width), rootSize.Width + TreeLabelSidePadding * 2.0);
        var items = new List<GraphPackItem>(children.Count);
        var moduleX = Math.Max(0, (width - modulePlan.Width) * 0.5);
        var fileX = Math.Max(0, (width - filePlan.Width) * 0.5);
        foreach (var item in filePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + fileX, item.Offset.Y)));
        }

        foreach (var item in modulePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + moduleX, item.Offset.Y + moduleY)));
        }

        var height = Math.Max(
            root.RootLabelOffsetY + rootSize.Height,
            modulePlan.Height > 0 ? moduleY + modulePlan.Height : 0);
        return new GraphPackPlan(items, width, height);
    }

    private GraphPackPlan CreateCubeRootModulePackPlan(
        IReadOnlyList<GraphNode> nodes,
        Func<GraphNode, Size> getSize,
        GraphPackPlan topPlan,
        Size rootSize)
    {
        if (nodes.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        var topGap = (topPlan.Height > 0 ? RootFileBlockGap : 0.0) + rootSize.Height + TreeChildGapY;
        return CreateCompactCubePackPlan(
            nodes,
            getSize,
            CubePackGap,
            CubePackGap,
            plan => new Size(
                Math.Max(Math.Max(topPlan.Width, rootSize.Width + TreeLabelSidePadding * 2.0), plan.Width),
                topPlan.Height + topGap + plan.Height));
    }

    private static GraphPackPlan CreateSingleRowPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double gap)
    {
        if (children.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        var items = new List<GraphPackItem>(children.Count);
        var x = 0.0;
        var height = 0.0;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (index > 0)
            {
                x += gap;
            }

            items.Add(new GraphPackItem(child, new Point(x, 0)));
            var size = getSize(child);
            x += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new GraphPackPlan(items, x, height);
    }

    private GraphPackPlan CreateUnmixedPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap,
        bool forceGrid)
    {
        return forceGrid
            ? CreateGridPackPlan(children, getSize, aspect, gap)
            : CreateMasonryPackPlan(children, getSize, aspect, gap, moduleGap);
    }

    private GraphPackPlan CreateMixedPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap)
    {
        var modules = children.Where(IsPackModule).ToArray();
        var files = children.Where(child => !IsPackModule(child)).ToArray();
        var modulePlan = modules.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateUnmixedPackPlan(modules, getSize, aspect, gap, moduleGap, forceGrid: false);
        var filePlan = files.Length == 0
            ? new GraphPackPlan([], 0, 0)
            : CreateUnmixedPackPlan(files, getSize, aspect, gap, moduleGap, ShouldUseFileGrid(files, getSize));
        var items = new List<GraphPackItem>(children.Count);

        foreach (var item in modulePlan.Items)
        {
            items.Add(item);
        }

        var fileY = modulePlan.Height > 0 && filePlan.Height > 0
            ? modulePlan.Height + moduleGap
            : modulePlan.Height;
        foreach (var item in filePlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X, item.Offset.Y + fileY)));
        }

        return new GraphPackPlan(
            items,
            Math.Max(modulePlan.Width, filePlan.Width),
            fileY + filePlan.Height);
    }

    private GraphPackPlan CreateGridPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap)
    {
        var cellWidth = children.Max(child => getSize(child).Width);
        var cellHeight = children.Max(child => getSize(child).Height);
        var columns = ChooseGridColumns(children.Count, cellWidth, cellHeight, aspect);
        var items = new List<GraphPackItem>(children.Count);

        for (var index = 0; index < children.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            items.Add(new GraphPackItem(
                children[index],
                new Point(column * (cellWidth + gap), row * (cellHeight + gap))));
        }

        var rows = (int)Math.Ceiling(children.Count / (double)columns);
        return new GraphPackPlan(
            items,
            columns * cellWidth + Math.Max(0, columns - 1) * gap,
            rows * cellHeight + Math.Max(0, rows - 1) * gap);
    }

    private GraphPackPlan CreateMasonryPackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double aspect,
        double gap,
        double moduleGap)
    {
        var items = children
            .Select(node => new SizedGraphNode(node, getSize(node)))
            .ToArray();
        var maxWidth = items.Max(item => item.Size.Width);
        var totalWidth = MeasureInlinePackWidth(items, gap, moduleGap);
        var totalArea = items.Sum(item => item.Size.Width * item.Size.Height);
        var desiredWidth = Math.Sqrt(Math.Max(1.0, totalArea) * Math.Max(0.8, aspect));
        var minWidth = Math.Min(totalWidth, Math.Max(maxWidth, items.Average(item => item.Size.Width) * 1.35));
        var maxCandidateWidth = Math.Max(minWidth, Math.Min(totalWidth, desiredWidth * 1.85));
        var candidates = new[]
            {
                minWidth,
                desiredWidth * 0.72,
                desiredWidth * 0.9,
                desiredWidth,
                desiredWidth * 1.16,
                desiredWidth * 1.36,
                maxCandidateWidth
            }
            .Select(width => Math.Clamp(width, minWidth, totalWidth))
            .Distinct()
            .ToArray();

        GraphPackPlan? best = null;
        var bestScore = double.MaxValue;
        foreach (var targetWidth in candidates)
        {
            var plan = CreateShelfPackPlan(items, targetWidth, gap, moduleGap);
            if (plan.Width <= 0 || plan.Height <= 0)
            {
                continue;
            }

            var actualAspect = plan.Width / Math.Max(1.0, plan.Height);
            var aspectPenalty = Math.Abs(Math.Log(Math.Max(0.05, actualAspect) / Math.Max(0.05, aspect)));
            var score = plan.Width * plan.Height * (1.0 + aspectPenalty * 0.22);
            if (score < bestScore)
            {
                best = plan;
                bestScore = score;
            }
        }

        return best ?? CreateShelfPackPlan(items, Math.Max(minWidth, desiredWidth), gap, moduleGap);
    }

    private static GraphPackPlan CreateLayeredPackPlan(
        GraphPackPlan topPlan,
        GraphPackPlan bottomPlan,
        double gap,
        int capacity)
    {
        if (topPlan.Items.Count == 0)
        {
            return bottomPlan;
        }

        if (bottomPlan.Items.Count == 0)
        {
            return topPlan;
        }

        var width = Math.Max(topPlan.Width, bottomPlan.Width);
        var bottomY = topPlan.Height + gap;
        var topX = Math.Max(0, (width - topPlan.Width) * 0.5);
        var bottomX = Math.Max(0, (width - bottomPlan.Width) * 0.5);
        var items = new List<GraphPackItem>(capacity);
        foreach (var item in topPlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + topX, item.Offset.Y)));
        }

        foreach (var item in bottomPlan.Items)
        {
            items.Add(new GraphPackItem(item.Node, new Point(item.Offset.X + bottomX, item.Offset.Y + bottomY)));
        }

        return new GraphPackPlan(items, width, bottomY + bottomPlan.Height);
    }

    private static GraphPackPlan CreateCompactCubePackPlan(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double gap,
        double moduleGap,
        Func<GraphPackPlan, Size>? measureFinalSize = null)
    {
        if (children.Count == 0)
        {
            return new GraphPackPlan([], 0, 0);
        }

        var candidates = CreateCompactPackCandidates(children, getSize, gap, moduleGap, maxCandidates: 96);
        GraphPackPlan? best = null;
        var bestPerimeter = double.MaxValue;
        var bestSquarePenalty = double.MaxValue;
        var bestArea = double.MaxValue;

        foreach (var plan in candidates)
        {
            var finalSize = measureFinalSize?.Invoke(plan) ?? new Size(plan.Width, plan.Height);
            if (finalSize.Width <= 0 || finalSize.Height <= 0)
            {
                continue;
            }

            var perimeter = finalSize.Width + finalSize.Height;
            var squarePenalty = MeasureSquarePenalty(finalSize.Width, finalSize.Height);
            var area = finalSize.Width * finalSize.Height;
            if (IsBetterCompactPlan(perimeter, squarePenalty, area, bestPerimeter, bestSquarePenalty, bestArea))
            {
                best = plan;
                bestPerimeter = perimeter;
                bestSquarePenalty = squarePenalty;
                bestArea = area;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return candidates.Count > 0 ? candidates[0] : new GraphPackPlan([], 0, 0);
    }

    private static IReadOnlyList<GraphPackPlan> CreateCompactPackCandidates(
        IReadOnlyList<GraphNode> children,
        Func<GraphNode, Size> getSize,
        double gap,
        double moduleGap,
        int maxCandidates)
    {
        if (children.Count == 0)
        {
            return [];
        }

        var items = children
            .Select(node => new SizedGraphNode(node, getSize(node)))
            .ToArray();
        return CreateCompactPackCandidates(items, gap, moduleGap, maxCandidates);
    }

    private static IReadOnlyList<GraphPackPlan> CreateCompactPackCandidates(
        IReadOnlyList<SizedGraphNode> items,
        double gap,
        double moduleGap,
        int maxCandidates)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var plans = new List<GraphPackPlan>();
        var seen = new HashSet<(long Width, long Height)>();
        foreach (var targetWidth in CreateCubeTargetWidthCandidates(items, gap, moduleGap))
        {
            AddPlan(CreateShelfPackPlan(items, targetWidth, gap, moduleGap));
        }

        return ReduceCompactPackCandidates(plans, maxCandidates);

        void AddPlan(GraphPackPlan plan)
        {
            var key = (Width: (long)Math.Round(plan.Width * 2.0), Height: (long)Math.Round(plan.Height * 2.0));
            if (seen.Add(key))
            {
                plans.Add(plan);
            }
        }
    }

    private static IReadOnlyList<GraphPackPlan> ReduceCompactPackCandidates(
        List<GraphPackPlan> plans,
        int maxCandidates)
    {
        if (plans.Count <= maxCandidates)
        {
            return plans;
        }

        var selected = new List<GraphPackPlan>(maxCandidates);
        var seen = new HashSet<(long Width, long Height)>();
        var bestCount = Math.Max(8, maxCandidates / 2);
        foreach (var plan in plans
            .OrderBy(plan => plan.Width + plan.Height)
            .ThenBy(plan => MeasureSquarePenalty(plan.Width, plan.Height))
            .ThenBy(plan => plan.Width * plan.Height)
            .Take(bestCount))
        {
            AddSelected(plan);
        }

        var byWidth = plans
            .OrderBy(plan => plan.Width)
            .ThenBy(plan => plan.Height)
            .ToArray();
        var remaining = maxCandidates - selected.Count;
        if (remaining > 0)
        {
            for (var index = 0; index < remaining; index++)
            {
                var sourceIndex = remaining == 1
                    ? byWidth.Length / 2
                    : (int)Math.Round(index * (byWidth.Length - 1) / (double)(remaining - 1));
                AddSelected(byWidth[sourceIndex]);
            }
        }

        return selected;

        void AddSelected(GraphPackPlan plan)
        {
            var key = (Width: (long)Math.Round(plan.Width * 2.0), Height: (long)Math.Round(plan.Height * 2.0));
            if (seen.Add(key))
            {
                selected.Add(plan);
            }
        }
    }

    private static double MeasureSquarePenalty(double width, double height)
    {
        return Math.Abs(Math.Log(Math.Max(0.05, width / Math.Max(1.0, height))));
    }

    private static bool IsBetterCompactPlan(
        double perimeter,
        double squarePenalty,
        double area,
        double bestPerimeter,
        double bestSquarePenalty,
        double bestArea)
    {
        return perimeter < bestPerimeter - 0.1
            || (Math.Abs(perimeter - bestPerimeter) <= 0.1 && squarePenalty < bestSquarePenalty - 0.001)
            || (Math.Abs(perimeter - bestPerimeter) <= 0.1
                && Math.Abs(squarePenalty - bestSquarePenalty) <= 0.001
                && area < bestArea);
    }

    private static SortedSet<double> CreateCubeTargetWidthCandidates(
        IReadOnlyList<SizedGraphNode> items,
        double gap,
        double moduleGap)
    {
        var candidates = new SortedSet<double>();
        var maxWidth = items.Max(item => Math.Max(1.0, item.Size.Width));
        var totalWidth = MeasureInlinePackWidth(items, gap, moduleGap);
        var totalArea = items.Sum(item => Math.Max(1.0, item.Size.Width) * Math.Max(1.0, item.Size.Height));

        void AddCandidate(double width)
        {
            if (!double.IsFinite(width))
            {
                return;
            }

            var normalized = Math.Round(Math.Clamp(width, maxWidth, totalWidth) * 2.0) / 2.0;
            candidates.Add(normalized);
        }

        AddCandidate(maxWidth);
        AddCandidate(totalWidth);
        var squareWidth = Math.Sqrt(Math.Max(1.0, totalArea));
        AddCandidate(squareWidth * 0.72);
        AddCandidate(squareWidth * 0.88);
        AddCandidate(squareWidth);
        AddCandidate(squareWidth * 1.12);
        AddCandidate(squareWidth * 1.32);

        for (var start = 0; start < items.Count; start++)
        {
            var width = 0.0;
            SizedGraphNode? previous = null;
            for (var end = start; end < items.Count; end++)
            {
                var item = items[end];
                if (previous is not null)
                {
                    width += GapBetween(previous.Value.Node, item.Node, gap, moduleGap);
                }

                width += item.Size.Width;
                AddCandidate(width);
                previous = item;
            }
        }

        return candidates;
    }

    private static GraphPackPlan CreateShelfPackPlan(
        IReadOnlyList<SizedGraphNode> items,
        double targetWidth,
        double gap,
        double moduleGap)
    {
        var placements = new List<GraphPackItem>(items.Count);
        var x = 0.0;
        var y = 0.0;
        var rowHeight = 0.0;
        var rowHasModule = false;
        var width = 0.0;
        SizedGraphNode? previous = null;

        foreach (var item in items)
        {
            var inlineGap = previous is null ? 0.0 : GapBetween(previous.Value.Node, item.Node, gap, moduleGap);
            if (x > 0 && x + inlineGap + item.Size.Width > targetWidth)
            {
                width = Math.Max(width, x);
                var rowGap = rowHasModule || IsPackModule(item.Node) ? moduleGap : gap;
                x = 0;
                y += rowHeight + rowGap;
                rowHeight = 0;
                rowHasModule = false;
                previous = null;
                inlineGap = 0;
            }

            x += inlineGap;
            placements.Add(new GraphPackItem(item.Node, new Point(x, y)));
            x += item.Size.Width;
            rowHeight = Math.Max(rowHeight, item.Size.Height);
            rowHasModule |= IsPackModule(item.Node);
            previous = item;
        }

        width = Math.Max(width, x);
        var height = y + rowHeight;
        return new GraphPackPlan(placements, width, height);
    }

    private static double MeasureInlinePackWidth(IReadOnlyList<SizedGraphNode> items, double gap, double moduleGap)
    {
        var width = 0.0;
        SizedGraphNode? previous = null;
        foreach (var item in items)
        {
            width += item.Size.Width;
            if (previous is not null)
            {
                width += GapBetween(previous.Value.Node, item.Node, gap, moduleGap);
            }

            previous = item;
        }

        return width;
    }

    private static double GapBetween(GraphNode previous, GraphNode next, double gap, double moduleGap)
    {
        return IsPackModule(previous) || IsPackModule(next) ? moduleGap : gap;
    }

    private static bool IsPackModule(GraphNode node)
    {
        return node.Children.Count > 0 || node.Node?.IsFolder == true;
    }

    private static bool ContinuesGeneration(GraphNode node)
    {
        return node.Children.Count > 0;
    }

    private static bool HasMixedPackKinds(IReadOnlyList<GraphNode> children)
    {
        var hasModule = false;
        var hasFile = false;
        foreach (var child in children)
        {
            if (IsPackModule(child))
            {
                hasModule = true;
            }
            else
            {
                hasFile = true;
            }

            if (hasModule && hasFile)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldUseFileGrid(IReadOnlyList<GraphNode> children, Func<GraphNode, Size> getSize)
    {
        return children.Count >= 8 && HasUniformPackSize(children, getSize);
    }

    private bool ShouldUseTreeGrid(IReadOnlyList<GraphNode> children)
    {
        if (children.Count < 10)
        {
            return false;
        }

        var leafish = children.Count(child => child.Children.Count == 0);
        return leafish / (double)children.Count >= 0.90
            && HasUniformPackSize(children, child => child.TreeSize);
    }

    private static bool HasUniformPackSize(IReadOnlyList<GraphNode> children, Func<GraphNode, Size> getSize)
    {
        var minWidth = double.MaxValue;
        var minHeight = double.MaxValue;
        var maxWidth = 0.0;
        var maxHeight = 0.0;
        foreach (var child in children)
        {
            var size = getSize(child);
            minWidth = Math.Min(minWidth, Math.Max(1.0, size.Width));
            minHeight = Math.Min(minHeight, Math.Max(1.0, size.Height));
            maxWidth = Math.Max(maxWidth, size.Width);
            maxHeight = Math.Max(maxHeight, size.Height);
        }

        return maxWidth / minWidth <= 1.55
            && maxHeight / minHeight <= 1.45;
    }

    private static int ChooseGridColumns(int count, double cellWidth, double cellHeight, double aspect)
    {
        var cellAspect = Math.Max(0.1, cellWidth / Math.Max(1.0, cellHeight));
        var desired = Math.Sqrt(count * aspect / cellAspect);
        return Math.Clamp((int)Math.Ceiling(desired), 1, Math.Min(count, 42));
    }

    private static Size MeasureNodeSize(GraphNode node)
    {
        if (node.IsAggregate)
        {
            return new Size(Math.Max(88, EstimateTextWidth(node.Title, 8.2) + 16), 22);
        }

        var meta = node.Node is null ? "" : BuildNodeMeta(node.Node);
        var titleFontSize = node.Depth == 0 ? 11.5 : 10.0;
        var titleWidth = EstimateTextWidth(node.Title, titleFontSize);
        var metaWidth = string.IsNullOrWhiteSpace(meta) ? 0.0 : EstimateTextWidth(meta, 7.7);
        var contentWidth = Math.Ceiling(Math.Max(titleWidth, metaWidth) + 18.0);

        if (node.Node?.IsFolder == true)
        {
            var size = node.Depth == 0 ? new Size(136, 34) : new Size(118, 28);
            return new Size(Math.Max(size.Width, contentWidth), size.Height);
        }

        return new Size(Math.Max(112, contentWidth), 30);
    }

    private static double EstimateTextWidth(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var width = 0.0;
        foreach (var character in text)
        {
            width += character switch
            {
                'W' or 'M' or 'w' or 'm' => fontSize * 0.82,
                'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '|' => fontSize * 0.32,
                ' ' => fontSize * 0.34,
                '_' or '-' or '/' or '\\' => fontSize * 0.50,
                _ => fontSize * 0.58
            };
        }

        return width;
    }

    private Rect CalculateContentBounds()
    {
        if (_nodes.Count == 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        var first = true;
        var left = 0.0;
        var top = 0.0;
        var right = 0.0;
        var bottom = 0.0;

        foreach (var node in _nodes)
        {
            Include(node.Bounds);
            if (node.RegionBounds.Width > 0 && node.RegionBounds.Height > 0)
            {
                Include(node.RegionBounds);
            }
        }

        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));

        void Include(Rect rect)
        {
            if (first)
            {
                left = rect.Left;
                top = rect.Top;
                right = rect.Right;
                bottom = rect.Bottom;
                first = false;
                return;
            }

            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }
    }

}
