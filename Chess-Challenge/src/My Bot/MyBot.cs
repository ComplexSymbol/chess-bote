using System;
using System.Linq;
using ChessChallenge.API;
using System.Collections.Generic;

//namespace ChessChallenge.Example;
public class MyBot : IChessBot
{
    Board board;
    Move bestMove;
    int[] pieceValues = { 100, 300, 320, 500, 900 };
    int maxDepth = 2;
    bool isEndgame, searchCanceled, playHarder, hasCastled;
    Timer timer;

    readonly ulong[,] PackedSquareBonusTable = {
        { 58233348458073600, 61037146059233280, 63851895826342400, 66655671952007680 },
        { 63862891026503730, 66665589183147058, 69480338950193202, 226499563094066 },
        { 63862895153701386, 69480338782421002, 5867015520979476,  8670770172137246 },
        { 63862916628537861, 69480338782749957, 8681765288087306,  11485519939245081 },
        { 63872833708024320, 69491333898698752, 8692760404692736,  11496515055522836 },
        { 63884885386256901, 69502350490469883, 5889005753862902,  8703755520970496 },
        { 63636395758376965, 63635334969551882, 21474836490,       1516 },
        { 58006849062751744, 63647386663573504, 63625396431020544, 63614422789579264 }
    };

    int GetSquareBonus(PieceType type, bool isWhite, int file, int rank)
    {
        if (isEndgame) return 0;

        if (file > 3)
            file = 7 - file;

        if (isWhite)
            rank = 7 - rank;

        sbyte unpackedData = unchecked((sbyte)((PackedSquareBonusTable[rank, file] >> 8 * ((int)type - 1)) & 0xFF));

        return isWhite ? unpackedData : -unpackedData;
    }


    public Move Think(Board b, Timer t)
    {
        isEndgame = false;
        board = b;
        bestMove = b.GetLegalMoves().OrderBy(_ => Guid.NewGuid()).ToList()[0];
        timer = t;
        searchCanceled = false;
        maxDepth = 2;

        //define because the argument needs to be ref
        ulong blackPiecesBB = b.BlackPiecesBitboard;

        //Checks if there are pieces in their opponent's territory
        playHarder = b.WhitePiecesBitboard > 0x100000000
                     || BitboardHelper.ClearAndGetIndexOfLSB(ref blackPiecesBB) < 32;

        Console.WriteLine();

        for (; ; maxDepth += 2)
        {
            Console.WriteLine("M: "
                              + Negamax(maxDepth, -10000, 10000)
                              + "; d = "
                              + maxDepth
                              + "; "
                              + "Best "
                              + bestMove);

            if (searchCanceled) break;
        }

        if (bestMove.IsCastles) hasCastled = true;
        return bestMove;
    }

    int Negamax(int depth, int alpha, int beta)
    {
        if ((timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / (playHarder ? 30 : 50)) || depth >= 1000) searchCanceled = true;
        if (searchCanceled) return 0;
        if (board.IsDraw()) return -250;
        if (board.IsInCheckmate()) return -10000 + (maxDepth - depth);
        if (depth == 0) return Evaluate();

        isEndgame = GetMaterials(board.GetAllPieceLists(), true) < 1000;
        Move[] sortedLegalMoves = SortMoves(board.GetLegalMoves());
        int eval, bestEval = -10000;

        foreach (Move responce in sortedLegalMoves)
        {
            board.MakeMove(responce);
            eval = -Negamax(depth - 1, -beta, -alpha);
            board.UndoMove(responce);

            if (eval > bestEval && !searchCanceled)
            {
                bestEval = eval;
                bestMove = (depth == maxDepth) ? responce : bestMove;
            }

            if (eval >= beta) return beta;

            alpha = Math.Max(alpha, eval);
        }

        return alpha;
    }

    int EndGameEval(Square ourKingSquare, Square oppKingSquare)
    {
        int eval = 0, file = oppKingSquare.File, rank = oppKingSquare.Rank;
        
        //Distance between opp king and wall
        eval += Math.Max(3 - file, file - 4) + Math.Max(3 - rank, rank - 4);

        //Distance between our king and opp king
        eval -= Math.Abs(ourKingSquare.Rank - rank) + Math.Abs(ourKingSquare.File - file);

        return isEndgame ? eval * 10 : 0;
    }

    int GetMaterials(PieceList[] pieceLists, bool onlyOpponent)
    {
        int materialAdvantage = 0, oppMaterial = 0;

        for (int i = 0; i < 5; i++)
        {
            int currentPieceVal = pieceValues[i];
            materialAdvantage += currentPieceVal * (pieceLists[i].Count - pieceLists[i + 6].Count);
            oppMaterial += currentPieceVal * pieceLists[i + (onlyOpponent ? (board.IsWhiteToMove ? 6 : 0) : 6)].Count;
        }

        return onlyOpponent ? oppMaterial : materialAdvantage;
    }


    int Evaluate()
    {
        PieceList[] pieceLists = board.GetAllPieceLists();


        int squareBonus = 0, sum = (board.GetLegalMoves().Length / 10) + GetMaterials(pieceLists, false);
        
        int castlesVal = (!hasCastled && !board.HasKingsideCastleRight(board.IsWhiteToMove)) ? -50 : 50;

        //POV: you remove all curley braces
        foreach (PieceList pList in pieceLists)
            foreach (Piece piece in pList)
                if (!isEndgame)
                    squareBonus += GetSquareBonus(piece.PieceType, piece.IsWhite, piece.Square.File, piece.Square.Rank);

        //Fixes weird bug with squarebonuses for black
        if (!board.IsWhiteToMove) squareBonus *= -1;

        return ((board.IsWhiteToMove ? 1 : -1) * sum) + squareBonus +
            EndGameEval(board.GetKingSquare(board.IsWhiteToMove),
                        board.GetKingSquare(!board.IsWhiteToMove));
    }

    struct sortableMove
    {
        public int Ranking;
        public Move UnsortedMove;
    }

    Move[] SortMoves(Move[] movesToSort)
    {
        if (movesToSort.Length == 1) return movesToSort;

        List<sortableMove> unsortedMoves = new();

        //Rank each move and push it to rankings
        foreach (Move move in movesToSort)
        {
            int moveScoreGuess = 0;

            if (move.IsCapture)
                moveScoreGuess += 100 * (pieceValues[(int)move.CapturePieceType - 1] - (board.SquareIsAttackedByOpponent(move.TargetSquare) ? pieceValues[(int)move.MovePieceType - 1] : 0));

            if (move.IsPromotion)
                moveScoreGuess += 100 * pieceValues[(int)move.PromotionPieceType - 1];

            if (move.Equals(bestMove))
                moveScoreGuess += 100000;

            unsortedMoves.Add(new sortableMove { Ranking = moveScoreGuess, UnsortedMove = move});
        }

        //Sort moves by the moveScoreGuess
        var sortedMoves = unsortedMoves.OrderByDescending(o => o.Ranking)
                                       .ToList()
                                       .ConvertAll(s => s.UnsortedMove);

        return sortedMoves.ToArray();
    }
}
