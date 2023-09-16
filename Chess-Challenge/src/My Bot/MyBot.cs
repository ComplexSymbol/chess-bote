using System;
using System.Linq;
using System.Numerics;
using ChessChallenge.API;

//TODO: Run a profiler
//TODO: Tune the bot
//TODO: Add UCI & cute chess
//TODO: Integrate QSearch with main search function

//namespace ChessChallenge.Example;
public class MyBot : IChessBot
{
    Board board;
    Move bestMove;
    int[] pieceValues = { 100, 320, 330, 500, 900, 0 };
    int maxDepth,
        millisRemaining;
    bool isEndgame,
         searchCanceled,
         playHarder;
    Timer timer;

    struct Transposition
    {
        public ulong zobristHash;
        public int evaluation,
                   tMaxDepth,
                   depth;
        public sbyte flag;
    };

    Transposition[] TTable = new Transposition[0x7FFFFF + 1];

    ulong[,] PackedSquareBonusTable = {
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
        //mirror because square bonuses only contain files 0-3
        if (file > 3)
            file = 7 - file;

        //bonuses reflect black values
        if (isWhite)
            rank = 7 - rank;

        sbyte unpackedData = unchecked((sbyte)((PackedSquareBonusTable[rank, file]
                             >> 8
                             * ((int)type - 1))
                             & 0xFF));

        return isWhite ? unpackedData : -unpackedData;
    }


    public Move Think(Board b, Timer t)
    {
        isEndgame = false;
        board = b;
        bestMove = SortMoves(board.GetLegalMoves())[0];
        timer = t;
        searchCanceled = false;
        maxDepth = 1;
        millisRemaining = t.MillisecondsRemaining;


        //Checks if there are pieces in their opponent's territory
        playHarder = b.WhitePiecesBitboard > 0x100000000
                     || BitOperations.TrailingZeroCount(b.BlackPiecesBitboard) < 32;
                     
                       


        //DEBUG
        //Console.WriteLine();

        for (; ;)
        {
            //TODO: Optimize later

            
            //DEBUG
            
                int eval = Negamax(++maxDepth, -10000, 10000);

                string evalStr = eval.ToString();
                string sign = (Math.Abs(eval) >= 9980) ? (Math.Sign(eval) == 1 ? "+" : "-") : "";

                if (Math.Abs(eval) >= 9980) evalStr = "MATE IN " + Math.Floor(((double)(10000 - Math.Abs(eval)) / 2) + 1).ToString();

                Console.WriteLine("M: " + sign + evalStr
                                  + "; d = " + maxDepth
                                  + "; Best " + bestMove
                                  + "; in " + timer.MillisecondsElapsedThisTurn + "ms");

                if (searchCanceled || eval >= 9980) break;
            

            //Optimized version:
            //if (Negamax(++maxDepth, -10000, 10000) >= 9980 || searchCanceled) break;
        }

        return bestMove;
    }

    int Negamax(int depth, int alpha, int beta)
    {
        //Bunch of conditionals at the beginning to save computational time
        if (timer.MillisecondsElapsedThisTurn
             >= millisRemaining / (playHarder ? 30 : 45)
             || maxDepth >= 100) searchCanceled = true;
        if (searchCanceled) return 0;
        if (board.IsDraw()) return -250;
        if (board.IsInCheckmate()) return -10000 + (maxDepth - depth);
        if (depth == 0) return QSearch(alpha, beta);

        ref Transposition transposition = ref TTable[board.ZobristKey & 0x7FFFFF];
        int TE = transposition.evaluation, 
            eval,
            bestEval = -10000,
            startingAlpha = alpha;
        
        if (depth > 2
                && transposition.zobristHash == board.ZobristKey
                && transposition.depth < depth
                && transposition.tMaxDepth > maxDepth
                &&
                (transposition.flag == 1
                || (transposition.flag == 2 && TE > beta)
                || (transposition.flag == 3 && TE <= alpha))
           ) return TE;


        isEndgame = GetMaterials(board.GetAllPieceLists(), true) < 1000;

        foreach (Move response in SortMoves(board.GetLegalMoves()))
        {
            board.MakeMove(response);
            eval = -Negamax(depth - 1, -beta, -alpha);
            board.UndoMove(response);

            if (eval > bestEval && !searchCanceled)
            {
                bestEval = eval;
                bestMove = (depth == maxDepth) ? response : bestMove;
            }

            if (eval >= beta) return beta;

            alpha = Math.Max(alpha, eval);
        }

        transposition.evaluation = bestEval;
        transposition.zobristHash = board.ZobristKey;
        transposition.depth = depth;
        transposition.tMaxDepth = maxDepth;

        if (bestEval < startingAlpha)
            transposition.flag = 3; //upper bound

        else if (bestEval >= beta)
            transposition.flag = 2; //lower bound

        else transposition.flag = 1; //"exact" score


        return alpha;
    }

    int QSearch(int alpha, int beta)
    {
        int stand_pat = Evaluate(),
            score;

        if (stand_pat >= beta)
            return beta;
        if (alpha < stand_pat)
            alpha = stand_pat;

        foreach (Move capture in SortMoves(board.GetLegalMoves(true)))  {
            board.MakeMove(capture);
            score = -QSearch(-beta, -alpha);
            board.UndoMove(capture);

            if (score >= beta)
                return beta;
            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    int GetMaterials(PieceList[] pieceLists, bool onlyOpponent)
    {
        int sum = 0,
            i = 0; //saves one token lmao


        //TODO: Optimize: investigate slices
        for (; i < 5; i++) //Taking advantage of how a pieceList[] works
            sum += pieceValues[i] * (pieceLists[i + (onlyOpponent ? (board.IsWhiteToMove ? 6 : 0) : 0)].Count - (onlyOpponent ? 0 : pieceLists[i + 6].Count));

        return sum;
    }


    int Evaluate()
    {
        PieceList[] pieceLists = board.GetAllPieceLists();

        bool isWhiteToMove = board.IsWhiteToMove;

        Square ourKingSquare = board.GetKingSquare(isWhiteToMove),
               oppKingSquare = board.GetKingSquare(!isWhiteToMove);

        int squareBonus = 0,
            sum = (board.GetLegalMoves().Length / 10) + GetMaterials(pieceLists, false),
            endGameVal = 0,
            file = oppKingSquare.File,
            rank = oppKingSquare.Rank;




        //Piece Square-Bonus value calculations
            foreach (PieceList pList in pieceLists)
                foreach (Piece piece in pList)
                    if (!isEndgame)
                        squareBonus += GetSquareBonus(piece.PieceType, piece.IsWhite, piece.Square.File, piece.Square.Rank);

            //Fixes weird bug with square-bonuses for black
            if (!isWhiteToMove) squareBonus *= -1;



        //Endgame stuff
        if (isEndgame)
        {
            //Distance between opp king and wall
            endGameVal += Math.Max(3 - file, file - 4)
                          + Math.Max(3 - rank, rank - 4);

            //Distance between our king and opp king
            endGameVal -= Math.Abs(ourKingSquare.Rank - rank)
                          + Math.Abs(ourKingSquare.File - file);
        }

        endGameVal *= 7;

        return ((isWhiteToMove ? 1 : -1) * sum) + squareBonus + endGameVal;
    }

    struct sortableMove
    {
        public Move UnsortedMove;
        public int Ranking;
    }

    Move[] SortMoves(Move[] movesToSort)
    {
        //TODO: Optimize (time)
        //Avoid "new" operator

        var sortableMoves = new sortableMove[movesToSort.Length];

        int i = -1;

        foreach (Move move in movesToSort)
        {
            int moveScoreGuess = 0;

            //uses -= for moveScoreGuess so that you don't have to do .Reverse() at the end
            if (move.Equals(bestMove))
                moveScoreGuess -= 100000;

            if (move.IsCapture)
                moveScoreGuess -= 100 * (pieceValues[(int)move.CapturePieceType - 1] - pieceValues[(int)move.MovePieceType - 1]);

            if (move.IsPromotion)
                moveScoreGuess -= 100 * pieceValues[(int)move.PromotionPieceType - 1];

            sortableMoves[++i] = new sortableMove { Ranking = moveScoreGuess, UnsortedMove = move };
        }


        //Sort moves by the moveScoreGuess
        //Convert all elements to the Move element

        Array.Sort(sortableMoves, (x, y) => x.Ranking.CompareTo(y.Ranking));

        return sortableMoves.Select(a => a.UnsortedMove).ToArray();
    }
}
