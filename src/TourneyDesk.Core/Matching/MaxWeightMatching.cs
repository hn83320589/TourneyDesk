namespace TourneyDesk.Core.Matching;

public readonly record struct WeightedEdge(int U, int V, Int128 Weight);

/// <param name="Mate">Mate[v] = 與 v 配對的點；未配對為 -1。</param>
/// <param name="Tight">Tight[k] = 第 k 條邊在最終對偶解下的完整 slack 為 0。
/// 任何最佳（完美）匹配只會使用緊邊，因此可用來縮小後續求解的圖。</param>
public sealed record MatchingSolution(int[] Mate, bool[] Tight);

/// <summary>
/// 一般圖最大權匹配（Edmonds blossom，primal-dual，O(n³)）。
/// 演算法結構依 Joris van Rantwijk 的 mwmatching（Galil 1986 的說明）以 C# 重寫，權重為 <see cref="Int128"/> 整數。
/// 所有權重必須是偶數，以保證對偶變數全程為整數。
/// <paramref name="maxCardinality"/> = true 時回傳「最大基數匹配中權重最大者」；在完全圖上即為最大權完美匹配。
/// </summary>
public sealed class MaxWeightMatching
{
    private readonly int _n;
    private readonly WeightedEdge[] _edges;
    private readonly bool _maxCardinality;
    private readonly CancellationToken _ct;

    private readonly int[] _endpoint;
    private readonly List<int>[] _neighbend;
    private readonly int[] _mate;
    private readonly int[] _label;
    private readonly int[] _labelend;
    private readonly int[] _inblossom;
    private readonly int[] _blossomparent;
    private readonly List<int>?[] _blossomchilds;
    private readonly int[] _blossombase;
    private readonly List<int>?[] _blossomendps;
    private readonly int[] _bestedge;
    private readonly List<int>?[] _blossombestedges;
    private readonly Stack<int> _unusedblossoms;
    private readonly Int128[] _dualvar;
    private readonly bool[] _allowedge;
    private readonly List<int> _queue = [];
    private readonly List<int> _leafBuffer = [];

    private MaxWeightMatching(int vertexCount, WeightedEdge[] edges, bool maxCardinality, CancellationToken ct)
    {
        _n = vertexCount;
        _edges = edges;
        _maxCardinality = maxCardinality;
        _ct = ct;

        var n = vertexCount;
        var maxweight = Int128.Zero;
        foreach (var e in edges)
        {
            if (e.U == e.V || e.U < 0 || e.V < 0 || e.U >= n || e.V >= n)
                throw new ArgumentException($"不合法的邊 ({e.U},{e.V})。", nameof(edges));
            if (!Int128.IsEvenInteger(e.Weight))
                throw new ArgumentException("權重必須是偶數。", nameof(edges));
            if (e.Weight > maxweight)
                maxweight = e.Weight;
        }

        _endpoint = new int[2 * edges.Length];
        for (var p = 0; p < _endpoint.Length; p++)
            _endpoint[p] = p % 2 == 0 ? edges[p / 2].U : edges[p / 2].V;

        _neighbend = new List<int>[n];
        for (var i = 0; i < n; i++)
            _neighbend[i] = [];
        for (var k = 0; k < edges.Length; k++)
        {
            _neighbend[edges[k].U].Add(2 * k + 1);
            _neighbend[edges[k].V].Add(2 * k);
        }

        _mate = Filled(n, -1);
        _label = new int[2 * n];
        _labelend = Filled(2 * n, -1);
        _inblossom = new int[n];
        for (var i = 0; i < n; i++)
            _inblossom[i] = i;
        _blossomparent = Filled(2 * n, -1);
        _blossomchilds = new List<int>?[2 * n];
        _blossombase = Filled(2 * n, -1);
        for (var i = 0; i < n; i++)
            _blossombase[i] = i;
        _blossomendps = new List<int>?[2 * n];
        _bestedge = Filled(2 * n, -1);
        _blossombestedges = new List<int>?[2 * n];
        _unusedblossoms = new Stack<int>(Enumerable.Range(n, n));
        _dualvar = new Int128[2 * n];
        for (var i = 0; i < n; i++)
            _dualvar[i] = maxweight;
        _allowedge = new bool[edges.Length];
    }

    public static MatchingSolution Solve(
        int vertexCount,
        IReadOnlyList<WeightedEdge> edges,
        bool maxCardinality,
        CancellationToken cancellationToken = default)
    {
        var solver = new MaxWeightMatching(vertexCount, [.. edges], maxCardinality, cancellationToken);
        solver.Run();
        return new MatchingSolution(solver.ResultMates(), solver.TightEdges());
    }

    private static int[] Filled(int length, int value)
    {
        var a = new int[length];
        Array.Fill(a, value);
        return a;
    }

    private static T At<T>(List<T> list, int index) => list[((index % list.Count) + list.Count) % list.Count];

    private Int128 Slack(int k)
    {
        var e = _edges[k];
        return _dualvar[e.U] + _dualvar[e.V] - 2 * e.Weight;
    }

    private void Leaves(int b, List<int> output)
    {
        if (b < _n)
        {
            output.Add(b);
            return;
        }

        foreach (var t in _blossomchilds[b]!)
        {
            if (t < _n)
                output.Add(t);
            else
                Leaves(t, output);
        }
    }

    private List<int> LeavesOf(int b)
    {
        var list = new List<int>();
        Leaves(b, list);
        return list;
    }

    private void AssignLabel(int w, int t, int p)
    {
        while (true)
        {
            var b = _inblossom[w];
            _label[w] = _label[b] = t;
            _labelend[w] = _labelend[b] = p;
            _bestedge[w] = _bestedge[b] = -1;
            if (t == 1)
            {
                _leafBuffer.Clear();
                Leaves(b, _leafBuffer);
                _queue.AddRange(_leafBuffer);
                return;
            }

            // t == 2：其 base 的配對點標記為 S
            var mateOfBase = _mate[_blossombase[b]];
            w = _endpoint[mateOfBase];
            t = 1;
            p = mateOfBase ^ 1;
        }
    }

    private int ScanBlossom(int v, int w)
    {
        var path = new List<int>();
        var bas = -1;
        while (v != -1 || w != -1)
        {
            var b = _inblossom[v];
            if ((_label[b] & 4) != 0)
            {
                bas = _blossombase[b];
                break;
            }

            path.Add(b);
            _label[b] = 5;
            if (_labelend[b] == -1)
            {
                v = -1;
            }
            else
            {
                v = _endpoint[_labelend[b]];
                b = _inblossom[v];
                v = _endpoint[_labelend[b]];
            }

            if (w != -1)
                (v, w) = (w, v);
        }

        foreach (var b in path)
            _label[b] = 1;
        return bas;
    }

    private void AddBlossom(int bas, int k)
    {
        var v = _edges[k].U;
        var w = _edges[k].V;
        var bb = _inblossom[bas];
        var bv = _inblossom[v];
        var bw = _inblossom[w];
        var b = _unusedblossoms.Pop();
        _blossombase[b] = bas;
        _blossomparent[b] = -1;
        _blossomparent[bb] = b;
        var path = new List<int>();
        var endps = new List<int>();
        _blossomchilds[b] = path;
        _blossomendps[b] = endps;

        while (bv != bb)
        {
            _blossomparent[bv] = b;
            path.Add(bv);
            endps.Add(_labelend[bv]);
            v = _endpoint[_labelend[bv]];
            bv = _inblossom[v];
        }

        path.Add(bb);
        path.Reverse();
        endps.Reverse();
        endps.Add(2 * k);

        while (bw != bb)
        {
            _blossomparent[bw] = b;
            path.Add(bw);
            endps.Add(_labelend[bw] ^ 1);
            w = _endpoint[_labelend[bw]];
            bw = _inblossom[w];
        }

        _label[b] = 1;
        _labelend[b] = _labelend[bb];
        _dualvar[b] = 0;
        foreach (var leaf in LeavesOf(b))
        {
            if (_label[_inblossom[leaf]] == 2)
                _queue.Add(leaf);
            _inblossom[leaf] = b;
        }

        var bestedgeto = Filled(2 * _n, -1);
        foreach (var child in path)
        {
            IEnumerable<IEnumerable<int>> nblists = _blossombestedges[child] is { } best
                ? [best]
                : LeavesOf(child).Select(leaf => _neighbend[leaf].Select(p => p / 2));
            foreach (var nblist in nblists)
            {
                foreach (var kk in nblist)
                {
                    var i = _edges[kk].U;
                    var j = _edges[kk].V;
                    if (_inblossom[j] == b)
                        (i, j) = (j, i);
                    var bj = _inblossom[j];
                    if (bj != b && _label[bj] == 1 && (bestedgeto[bj] == -1 || Slack(kk) < Slack(bestedgeto[bj])))
                        bestedgeto[bj] = kk;
                }
            }

            _blossombestedges[child] = null;
            _bestedge[child] = -1;
        }

        var bestList = bestedgeto.Where(x => x != -1).ToList();
        _blossombestedges[b] = bestList;
        _bestedge[b] = -1;
        foreach (var kk in bestList)
            if (_bestedge[b] == -1 || Slack(kk) < Slack(_bestedge[b]))
                _bestedge[b] = kk;
    }

    private void ExpandBlossom(int b, bool endstage)
    {
        var childs = _blossomchilds[b]!;
        foreach (var s in childs)
        {
            _blossomparent[s] = -1;
            if (s < _n)
                _inblossom[s] = s;
            else if (endstage && _dualvar[s] == 0)
                ExpandBlossom(s, endstage);
            else
                foreach (var leaf in LeavesOf(s))
                    _inblossom[leaf] = s;
        }

        if (!endstage && _label[b] == 2)
        {
            var endps = _blossomendps[b]!;
            var entrychild = _inblossom[_endpoint[_labelend[b] ^ 1]];
            var j = childs.IndexOf(entrychild);
            int jstep, endptrick;
            if ((j & 1) != 0)
            {
                j -= childs.Count;
                jstep = 1;
                endptrick = 0;
            }
            else
            {
                jstep = -1;
                endptrick = 1;
            }

            var p = _labelend[b];
            while (j != 0)
            {
                _label[_endpoint[p ^ 1]] = 0;
                _label[_endpoint[At(endps, j - endptrick) ^ endptrick ^ 1]] = 0;
                AssignLabel(_endpoint[p ^ 1], 2, p);
                _allowedge[At(endps, j - endptrick) / 2] = true;
                j += jstep;
                p = At(endps, j - endptrick) ^ endptrick;
                _allowedge[p / 2] = true;
                j += jstep;
            }

            var bv = At(childs, j);
            _label[_endpoint[p ^ 1]] = _label[bv] = 2;
            _labelend[_endpoint[p ^ 1]] = _labelend[bv] = p;
            _bestedge[bv] = -1;
            j += jstep;
            while (At(childs, j) != entrychild)
            {
                bv = At(childs, j);
                if (_label[bv] == 1)
                {
                    j += jstep;
                    continue;
                }

                var labelled = LeavesOf(bv).FirstOrDefault(leaf => _label[leaf] != 0, -1);
                if (labelled != -1)
                {
                    _label[labelled] = 0;
                    _label[_endpoint[_mate[_blossombase[bv]]]] = 0;
                    AssignLabel(labelled, 2, _labelend[labelled]);
                }

                j += jstep;
            }
        }

        _label[b] = _labelend[b] = -1;
        _blossomchilds[b] = _blossomendps[b] = null;
        _blossombase[b] = -1;
        _blossombestedges[b] = null;
        _bestedge[b] = -1;
        _unusedblossoms.Push(b);
    }

    private void AugmentBlossom(int b, int v)
    {
        var t = v;
        while (_blossomparent[t] != b)
            t = _blossomparent[t];
        if (t >= _n)
            AugmentBlossom(t, v);

        var childs = _blossomchilds[b]!;
        var endps = _blossomendps[b]!;
        var i = childs.IndexOf(t);
        var j = i;
        int jstep, endptrick;
        if ((i & 1) != 0)
        {
            j -= childs.Count;
            jstep = 1;
            endptrick = 0;
        }
        else
        {
            jstep = -1;
            endptrick = 1;
        }

        while (j != 0)
        {
            j += jstep;
            t = At(childs, j);
            var p = At(endps, j - endptrick) ^ endptrick;
            if (t >= _n)
                AugmentBlossom(t, _endpoint[p]);
            j += jstep;
            t = At(childs, j);
            if (t >= _n)
                AugmentBlossom(t, _endpoint[p ^ 1]);
            _mate[_endpoint[p]] = p ^ 1;
            _mate[_endpoint[p ^ 1]] = p;
        }

        _blossomchilds[b] = [.. childs.Skip(i), .. childs.Take(i)];
        _blossomendps[b] = [.. endps.Skip(i), .. endps.Take(i)];
        _blossombase[b] = _blossombase[_blossomchilds[b]![0]];
    }

    private void AugmentMatching(int k)
    {
        foreach (var (start, startP) in new[] { (_edges[k].U, 2 * k + 1), (_edges[k].V, 2 * k) })
        {
            var s = start;
            var p = startP;
            while (true)
            {
                var bs = _inblossom[s];
                if (bs >= _n)
                    AugmentBlossom(bs, s);
                _mate[s] = p;
                if (_labelend[bs] == -1)
                    break;
                var t = _endpoint[_labelend[bs]];
                var bt = _inblossom[t];
                s = _endpoint[_labelend[bt]];
                var j = _endpoint[_labelend[bt] ^ 1];
                if (bt >= _n)
                    AugmentBlossom(bt, j);
                _mate[j] = _labelend[bt];
                p = _labelend[bt] ^ 1;
            }
        }
    }

    private void Run()
    {
        var n = _n;
        for (var stage = 0; stage < n; stage++)
        {
            _ct.ThrowIfCancellationRequested();

            Array.Clear(_label);
            Array.Fill(_bestedge, -1);
            for (var b = n; b < 2 * n; b++)
                _blossombestedges[b] = null;
            Array.Clear(_allowedge);
            _queue.Clear();

            for (var v = 0; v < n; v++)
                if (_mate[v] == -1 && _label[_inblossom[v]] == 0)
                    AssignLabel(v, 1, -1);

            var augmented = false;
            while (true)
            {
                while (_queue.Count > 0 && !augmented)
                {
                    var v = _queue[^1];
                    _queue.RemoveAt(_queue.Count - 1);
                    foreach (var p in _neighbend[v])
                    {
                        var k = p / 2;
                        var w = _endpoint[p];
                        if (_inblossom[v] == _inblossom[w])
                            continue;
                        var kslack = Int128.Zero;
                        if (!_allowedge[k])
                        {
                            kslack = Slack(k);
                            if (kslack <= 0)
                                _allowedge[k] = true;
                        }

                        if (_allowedge[k])
                        {
                            if (_label[_inblossom[w]] == 0)
                            {
                                AssignLabel(w, 2, p ^ 1);
                            }
                            else if (_label[_inblossom[w]] == 1)
                            {
                                var bas = ScanBlossom(v, w);
                                if (bas >= 0)
                                {
                                    AddBlossom(bas, k);
                                }
                                else
                                {
                                    AugmentMatching(k);
                                    augmented = true;
                                    break;
                                }
                            }
                            else if (_label[w] == 0)
                            {
                                _label[w] = 2;
                                _labelend[w] = p ^ 1;
                            }
                        }
                        else if (_label[_inblossom[w]] == 1)
                        {
                            var b = _inblossom[v];
                            if (_bestedge[b] == -1 || kslack < Slack(_bestedge[b]))
                                _bestedge[b] = k;
                        }
                        else if (_label[w] == 0)
                        {
                            if (_bestedge[w] == -1 || kslack < Slack(_bestedge[w]))
                                _bestedge[w] = k;
                        }
                    }
                }

                if (augmented)
                    break;

                var deltatype = -1;
                Int128 delta = 0;
                var deltaedge = -1;
                var deltablossom = -1;

                if (!_maxCardinality)
                {
                    deltatype = 1;
                    delta = MinVertexDual();
                }

                for (var v = 0; v < n; v++)
                {
                    if (_label[_inblossom[v]] == 0 && _bestedge[v] != -1)
                    {
                        var d = Slack(_bestedge[v]);
                        if (deltatype == -1 || d < delta)
                        {
                            delta = d;
                            deltatype = 2;
                            deltaedge = _bestedge[v];
                        }
                    }
                }

                for (var b = 0; b < 2 * n; b++)
                {
                    if (_blossomparent[b] == -1 && _label[b] == 1 && _bestedge[b] != -1)
                    {
                        var kslack = Slack(_bestedge[b]);
                        if (!Int128.IsEvenInteger(kslack))
                            throw new InvalidOperationException("內部錯誤：S-S 邊的 slack 不是偶數。");
                        var d = kslack / 2;
                        if (deltatype == -1 || d < delta)
                        {
                            delta = d;
                            deltatype = 3;
                            deltaedge = _bestedge[b];
                        }
                    }
                }

                for (var b = n; b < 2 * n; b++)
                {
                    if (_blossombase[b] >= 0 && _blossomparent[b] == -1 && _label[b] == 2
                        && (deltatype == -1 || _dualvar[b] < delta))
                    {
                        delta = _dualvar[b];
                        deltatype = 4;
                        deltablossom = b;
                    }
                }

                if (deltatype == -1)
                {
                    deltatype = 1;
                    delta = Int128.Max(0, MinVertexDual());
                }

                for (var v = 0; v < n; v++)
                {
                    var lv = _label[_inblossom[v]];
                    if (lv == 1)
                        _dualvar[v] -= delta;
                    else if (lv == 2)
                        _dualvar[v] += delta;
                }

                for (var b = n; b < 2 * n; b++)
                {
                    if (_blossombase[b] >= 0 && _blossomparent[b] == -1)
                    {
                        if (_label[b] == 1)
                            _dualvar[b] += delta;
                        else if (_label[b] == 2)
                            _dualvar[b] -= delta;
                    }
                }

                if (deltatype == 1)
                    break;

                if (deltatype == 2)
                {
                    _allowedge[deltaedge] = true;
                    var i = _edges[deltaedge].U;
                    var j = _edges[deltaedge].V;
                    if (_label[_inblossom[i]] == 0)
                        (i, j) = (j, i);
                    _queue.Add(i);
                }
                else if (deltatype == 3)
                {
                    _allowedge[deltaedge] = true;
                    _queue.Add(_edges[deltaedge].U);
                }
                else
                {
                    ExpandBlossom(deltablossom, false);
                }
            }

            if (!augmented)
                break;

            for (var b = n; b < 2 * n; b++)
            {
                if (_blossomparent[b] == -1 && _blossombase[b] >= 0 && _label[b] == 1 && _dualvar[b] == 0)
                    ExpandBlossom(b, true);
            }
        }
    }

    private Int128 MinVertexDual()
    {
        var min = _dualvar[0];
        for (var v = 1; v < _n; v++)
            if (_dualvar[v] < min)
                min = _dualvar[v];
        return min;
    }

    private int[] ResultMates()
    {
        var result = new int[_n];
        for (var v = 0; v < _n; v++)
            result[v] = _mate[v] >= 0 ? _endpoint[_mate[v]] : -1;
        return result;
    }

    /// <summary>完整 slack = dual(i) + dual(j) − 2w + 2 Σ z(B)（B 為同時包含 i、j 的 blossom）。</summary>
    private bool[] TightEdges()
    {
        var tight = new bool[_edges.Length];
        var chainI = new List<int>();
        var chainJ = new List<int>();
        for (var k = 0; k < _edges.Length; k++)
        {
            var s = Slack(k);
            Chain(_edges[k].U, chainI);
            Chain(_edges[k].V, chainJ);
            for (int a = chainI.Count - 1, b = chainJ.Count - 1; a >= 0 && b >= 0 && chainI[a] == chainJ[b]; a--, b--)
                s += 2 * _dualvar[chainI[a]];
            if (s < 0)
                throw new InvalidOperationException("內部錯誤：對偶解不可行。");
            tight[k] = s == 0;
        }

        return tight;
    }

    private void Chain(int v, List<int> chain)
    {
        chain.Clear();
        var b = _blossomparent[v];
        while (b != -1)
        {
            chain.Add(b);
            b = _blossomparent[b];
        }
    }
}
