namespace Gfold;

// Builds a sparse matrix in the column-compressed storage (CCS) format ECOS
// expects, from arbitrary-order (row, col, value) triplets. Duplicate entries
// at the same position are summed; explicit zeros are kept (harmless).
public sealed class SparseCcs
{
    public int Rows { get; }
    public int Cols { get; }

    private readonly List<(int Row, double Value)>[] _columns;

    public SparseCcs(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        _columns = new List<(int, double)>[cols];
        for (int j = 0; j < cols; j++)
            _columns[j] = new List<(int, double)>();
    }

    public void Add(int row, int col, double value)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols)
            throw new ArgumentOutOfRangeException($"({row},{col}) outside {Rows}x{Cols}");
        _columns[col].Add((row, value));
    }

    // (values, column pointers of length Cols+1, row indices), rows sorted
    // ascending within each column as ECOS requires.
    public (double[] Pr, int[] Jc, int[] Ir) Build()
    {
        var pr = new List<double>();
        var ir = new List<int>();
        var jc = new int[Cols + 1];
        for (int j = 0; j < Cols; j++)
        {
            jc[j] = pr.Count;
            foreach (var group in _columns[j]
                         .GroupBy(e => e.Row)
                         .OrderBy(g => g.Key))
            {
                ir.Add(group.Key);
                pr.Add(group.Sum(e => e.Value));
            }
        }
        jc[Cols] = pr.Count;
        return (pr.ToArray(), jc, ir.ToArray());
    }
}
