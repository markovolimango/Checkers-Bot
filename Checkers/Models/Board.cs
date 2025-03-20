using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Console = System.Console;

namespace Checkers.Models;

public class Board
{
    private const ulong TopEdge = 0x00000000000000FF;
    private const ulong BottomEdge = 0xFF00000000000000;
    private const ulong LeftEdge = 0x0101010101010101;
    private const ulong RightEdge = 0x8080808080808080;
    private const ulong Edges = TopEdge | BottomEdge | LeftEdge | RightEdge;

    private readonly List<Move> _kingsMoves;
    private readonly List<Move> _menMoves;

    private readonly ulong[] _pieces = new ulong[5];
    private bool _hasFoundCapture;

    public Board()
    {
        _pieces[(byte)Piece.Empty] = 0xAA55AAFFFF55AA55;
        _pieces[(byte)Piece.BlackMan] = 0x0000000000AA55AA;
        _pieces[(byte)Piece.RedMan] = 0x55AA550000000000;
        _pieces[(byte)Piece.BlackKing] = 0x0000000000000000;
        _pieces[(byte)Piece.RedKing] = 0x0000000000000000;
        IsBlackTurn = false;
        _menMoves = new List<Move>(10);
        _kingsMoves = new List<Move>(5);
        FindAllLegalMoves();
    }

    public Board(Board other)
    {
        for (var i = 0; i < 5; i++)
            _pieces[i] = other._pieces[i];
        IsBlackTurn = other.IsBlackTurn;
        _menMoves = other._menMoves;
        _kingsMoves = other._kingsMoves;
    }

    public Board(byte[,] pieces, bool isBlackTurn)
    {
        for (var row = 0; row < 8; row++)
        for (var col = 0; col < 8; col++)
            this[row, col] = (Piece)pieces[row, col];
        IsBlackTurn = isBlackTurn;
        _menMoves = new List<Move>(10);
        _kingsMoves = new List<Move>(5);
        FindAllLegalMoves();
    }

    public bool IsBlackTurn { get; private set; }
    private ulong BlackPieces => _pieces[(byte)Piece.BlackMan] | _pieces[(byte)Piece.BlackKing];
    private ulong RedPieces => _pieces[(byte)Piece.RedMan] | _pieces[(byte)Piece.RedKing];
    public bool IsBlackWin => BlackPieces == 0;
    public bool IsRedWin => RedPieces == 0;

    public Piece this[ulong mask]
    {
        get
        {
            if ((mask & (mask - 1)) != 0)
                throw new IndexOutOfRangeException("Invalid mask");
            for (byte i = 0; i < 5; i++)
                if ((_pieces[i] & mask) != 0)
                    return (Piece)i;
            throw new IndexOutOfRangeException("Invalid mask");
        }
        private set
        {
            var piece = (byte)value;
            if (((mask & TopEdge) != 0 && value == Piece.RedMan) ||
                ((mask & BottomEdge) != 0 && value == Piece.BlackMan))
                piece++;
            var inverse = ~mask;
            for (byte i = 0; i < 5; i++)
                _pieces[i] &= inverse;
            _pieces[piece] |= mask;
        }
    }

    public Piece this[byte index]
    {
        get => this[1UL << index];
        private set => this[1UL << index] = value;
    }

    public Piece this[int row, int col]
    {
        get => this[ToMask(row, col)];
        private set => this[ToMask(row, col)] = value;
    }

    public static ulong ToMask(int row, int col)
    {
        return 1UL << (row * 8 + col);
    }

    public static byte ToIndex(ulong mask)
    {
        return (byte)BitOperations.TrailingZeroCount(mask);
    }

    public static (int row, int col) ToPos(ulong mask)
    {
        var index = ToIndex(mask);
        return (index / 8, index % 8);
    }

    public static (int row, int col) ToPos(byte index)
    {
        return (index / 8, index % 8);
    }

    private ulong FindTargetSquares(ulong mask, Piece piece)
    {
        ulong res = 0;
        if ((mask & TopEdge) == 0 && piece != Piece.BlackMan)
        {
            if ((mask & LeftEdge) == 0)
                res |= mask >> 9;
            if ((mask & RightEdge) == 0)
                res |= mask >> 7;
        }

        if ((mask & BottomEdge) == 0 && piece != Piece.RedMan)
        {
            if ((mask & LeftEdge) == 0)
                res |= mask << 7;
            if ((mask & RightEdge) == 0)
                res |= mask << 9;
        }

        if (piece.IsBlack())
            return res & ~BlackPieces;
        return res & ~RedPieces;
    }

    private (ulong destinations, ulong captures) FindJumps(ulong mask, Piece piece, ulong targets)
    {
        ulong captures = 0, destinations = 0;
        if (!piece.IsBlack())
            targets &= BlackPieces;
        else
            targets &= RedPieces;

        foreach (var enemyTarget in GetPieceMasks(targets))
        {
            if ((enemyTarget & Edges) != 0)
                continue;
            var dir = ToIndex(enemyTarget) - ToIndex(mask);
            ulong dest = 0;
            if (dir >= 0)
                dest = (enemyTarget << dir) & _pieces[(byte)Piece.Empty];
            else
                dest = (enemyTarget >> -dir) & _pieces[(byte)Piece.Empty];
            if (dest != 0)
            {
                destinations |= dest;
                captures |= enemyTarget;
            }
        }

        return (destinations, captures);
    }

    private void AddCaptureMoves(List<Move> moves, List<byte> path, ulong captures, ulong mask, Piece piece)
    {
        AddCaptureMoves(FindJumps(mask, piece, FindTargetSquares(mask, piece)), moves, path, captures, mask, piece);
    }

    private void AddCaptureMoves((ulong, ulong) jumps, List<Move> moves, List<byte> path, ulong captures, ulong mask,
        Piece piece)
    {
        var hasFoundCapture = false;
        foreach (var jump in GetPieceMasks(jumps))
        {
            if ((captures & jump.second) != 0)
                continue;
            hasFoundCapture = true;
            var myPath = new List<byte>(path);
            myPath.Add(ToIndex(jump.first));
            var myCaptures = captures | jump.second;
            AddCaptureMoves(moves, myPath, myCaptures, jump.first, piece);
        }

        if (!hasFoundCapture)
            moves.Add(new Move(path, captures));
    }

    private void FindAllLegalMoves()
    {
        Console.WriteLine("All legal moves:");
        _hasFoundCapture = false;
        if (IsBlackTurn)
        {
            FindLegalMoves(_pieces[(byte)Piece.BlackMan], Piece.BlackMan, _menMoves);
            FindLegalMoves(_pieces[(byte)Piece.BlackKing], Piece.BlackKing, _kingsMoves);
        }
        else
        {
            FindLegalMoves(_pieces[(byte)Piece.RedMan], Piece.RedMan, _menMoves);
            FindLegalMoves(_pieces[(byte)Piece.RedKing], Piece.RedKing, _kingsMoves);
        }
    }

    private void FindLegalMoves(ulong pieces, Piece piece, List<Move> moves)
    {
        moves.Clear();
        foreach (var mask in GetPieceMasks(pieces))
        {
            var targets = FindTargetSquares(mask, piece);
            var jumps = FindJumps(mask, piece, targets);
            if (jumps.destinations == 0)
            {
                if (_hasFoundCapture)
                    continue;
                foreach (var emptyTarget in GetPieceMasks(targets & _pieces[(byte)Piece.Empty]))
                    moves.Add(new Move([ToIndex(mask), ToIndex(emptyTarget)], 0));
            }
            else
            {
                if (!_hasFoundCapture)
                {
                    _menMoves.Clear();
                    _kingsMoves.Clear();
                    _hasFoundCapture = true;
                }

                AddCaptureMoves(jumps, moves, [ToIndex(mask)], 0, mask, piece);
            }
        }

        Console.WriteLine($"{piece} moves:");
        foreach (var move in moves)
            Console.WriteLine(move);
    }

    public List<Move> FindMovesStartingWith(List<byte> path)
    {
        return FindMovesStartingWith(path, _kingsMoves).Concat(FindMovesStartingWith(path, _menMoves)).ToList();
    }

    public List<Move> FindMovesStartingWith(List<byte> path, List<Move> moves)
    {
        var res = new List<Move>(3);
        var len = path.Count;
        foreach (var move in moves)
        {
            if (move.Path.Count < path.Count)
                continue;
            var i = 0;
            while (i < len)
            {
                if (move.Path[i] != path[i])
                    break;
                i++;
            }

            if (i == len)
                res.Add(move);
        }

        return res;
    }

    public void MakeMove(Move move)
    {
        var piece = this[move.Start];
        this[move.Start] = Piece.Empty;
        this[move.End] = piece;
        this[move.Captures] = Piece.Empty;
        IsBlackTurn = !IsBlackTurn;
        FindAllLegalMoves();
    }

    public static IEnumerable<ulong> GetPieceMasks(ulong pieces)
    {
        while (pieces != 0)
        {
            yield return pieces & ~(pieces - 1);
            pieces &= pieces - 1;
        }
    }

    public static IEnumerable<(ulong first, ulong second)> GetPieceMasks((ulong first, ulong second) pieces)
    {
        while (pieces.first != 0 && pieces.second != 0)
        {
            yield return (pieces.first & ~(pieces.first - 1), pieces.second & ~(pieces.second - 1));
            pieces.first &= pieces.first - 1;
            pieces.second &= pieces.second - 1;
        }
    }

    public static IEnumerable<byte> GetPieceIndexes(ulong pieces)
    {
        while (pieces != 0)
        {
            yield return ToIndex(pieces);
            pieces &= pieces - 1;
        }
    }

    public IEnumerable<byte> GetPieceIndexes(Piece piece)
    {
        var pieces = _pieces[(byte)piece];
        while (pieces != 0)
        {
            yield return ToIndex(pieces & ~(pieces - 1));
            pieces &= pieces - 1;
        }
    }

    public override string ToString()
    {
        var res = "{ " + (byte)this[0] + ", ";
        for (byte i = 1; i < 63; i++)
        {
            res += (byte)this[i];
            if (i % 8 == 7)
                res += " },\n{ ";
            else
                res += ", ";
        }

        res += (byte)this[63] + " }";
        return res;
    }
}