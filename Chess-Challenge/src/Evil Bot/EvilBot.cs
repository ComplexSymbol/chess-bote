using System;
using System.Linq;
using ChessChallenge.API;
using System.Collections.Generic;

namespace ChessChallenge.Example;
public class EvilBot : IChessBot
{
    Board board;
    Move bestMove;
    int[] pieceValues = { 100, 300, 300, 500, 900 };
    int turn;
    int maxDepth = 4; //SET THIS ONLY TO AN EVEN VALUE!!!
    int count = 0;
    bool isEndgame = false;

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
        if (file > 3)
            file = 7 - file;

        if (isWhite)
            rank = 7 - rank;

        sbyte unpackedData = unchecked((sbyte)((PackedSquareBonusTable[rank, file] >> 8 * ((int)type - 1)) & 0xFF));

        return isWhite ? unpackedData : -unpackedData;
    }

    public Move Think(Board b, Timer timer)
    {
        isEndgame = false;
        board = b;
        turn = b.IsWhiteToMove ? 1 : -1;
        bestMove = board.GetLegalMoves()[0];

        //if (isEndgame) maxDepth = 6;

        Console.WriteLine("E: " + Negamax(maxDepth, -10000, 10000) + "; " + count);

        return bestMove;
    }

    int Negamax(int depth, int alpha, int beta)
    {
        count++;

        isEndgame = GetMaterials(board.GetAllPieceLists())[1] < 1000 ? true : false;
        if (depth == 0) return Evaluate();
        if (board.IsInCheckmate()) return -10000;
        if (board.IsDraw()) return 0;

        Move[] sortedLegalMoves = SortMoves(board.GetLegalMoves());

        int eval;
        int bestEval = -10000;

        foreach (Move responce in sortedLegalMoves)
        {
            board.MakeMove(responce);
            eval = -Negamax(depth - 1, -beta, -alpha);
            board.UndoMove(responce);

            if (eval > bestEval)
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
        int eval = 0;

        int oppKingDstFromCntrFile = Math.Max(3 - oppKingSquare.File, oppKingSquare.File - 4);
        int oppKingDstFromCntrRank = Math.Max(3 - oppKingSquare.Rank, oppKingSquare.Rank - 4);

        int oppKingDstFromCenter = oppKingDstFromCntrFile + oppKingDstFromCntrRank;
        eval += oppKingDstFromCenter;

        int dstBetweenKings = Math.Abs(ourKingSquare.Rank - oppKingSquare.Rank) + Math.Abs(ourKingSquare.File - oppKingSquare.File);

        eval += 14 - dstBetweenKings;

        return isEndgame ? eval * 100 : 0;
    }

    int[] GetMaterials(PieceList[] pieceLists)
    {
        int materialAdvantage = 0;
        int totalMaterial = 0;

        for (int i = 0; i < 5; i++)
        {
            materialAdvantage += pieceValues[i] * (pieceLists[i].Count - pieceLists[i + 6].Count);
            totalMaterial += pieceValues[i] * (pieceLists[i].Count + pieceLists[i + 6].Count);
        }

        if (totalMaterial < 2000) isEndgame = true;

        int[] returnArr = { materialAdvantage, totalMaterial };
        return returnArr;
    }


    int Evaluate()
    {
        PieceList[] pieceLists = board.GetAllPieceLists();

        int mobilityIndex = board.GetLegalMoves().Length;
        int squareBonus = 0;
        int materialAdvantage = GetMaterials(pieceLists)[0];

        foreach (PieceList pList in pieceLists)
        {
            foreach (Piece piece in pList)
            {
                if (!isEndgame) squareBonus += GetSquareBonus(piece.PieceType, piece.IsWhite, piece.Square.File, piece.Square.Rank);
            }
        }

        return turn * (materialAdvantage + mobilityIndex / 10) + squareBonus + EndGameEval(board.GetKingSquare(board.IsWhiteToMove), board.GetKingSquare(!board.IsWhiteToMove));
    }

    struct sortableMove
    {
        public int Ranking { get; set; }
        public Move UnsortedMove { get; set; }
    }

    Move[] SortMoves(Move[] movesToSort)
    {
        if (movesToSort.Length == 1) return movesToSort;

        List<sortableMove> unsortedMoves = new();

        List<int> rankings = new();

        //Rank each move and push it to rankings

        foreach (Move move in movesToSort)
        {
            int moveScoreGuess = 0;

            if (move.IsCapture)
                moveScoreGuess += 100 * (pieceValues[(int)move.CapturePieceType - 1] - (board.SquareIsAttackedByOpponent(move.TargetSquare) ? pieceValues[(int)move.MovePieceType - 1] : 0));

            if (move.IsPromotion)
                moveScoreGuess += 100 * pieceValues[(int)move.PromotionPieceType - 1];

            rankings.Add(moveScoreGuess);
        }

        //Sort moves by the moveScoreGuess
        //Sorry for the messy code!

        for (int i = 0; i < rankings.Count; i++)
        {
            unsortedMoves.Add(new sortableMove { Ranking = rankings[i], UnsortedMove = movesToSort[i] });
        }

        unsortedMoves = unsortedMoves.OrderByDescending(o => o.Ranking).ToList();
        List<Move> sortedMoves = new();

        foreach (sortableMove s in unsortedMoves)
        {
            sortedMoves.Add(s.UnsortedMove);
        }

        return sortedMoves.ToArray();
    }
}