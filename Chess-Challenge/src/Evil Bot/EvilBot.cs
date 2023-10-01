using System;
using System.Linq;
using ChessChallenge.API;

namespace ChessChallenge.Example;
public class EvilBot : IChessBot
{

    // Piece values: pawn, knight, bishop, rook, queen, king
    private static readonly int[] pieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                                  94, 281, 297, 512, 936, 0 }; // Endgame

    private Board m_board;
    private Timer m_timer;

    private Move bestMoveRoot;
    private int bestEvalRoot;
    private Move bestIterativeMove;
    private int bestIterativeEval;

    private double searchMaxTime;

    private static int positionsEvaled;
    private int TTused;

    // Compressed Piece-Square tables used for evaluation, ComPresSTO
    private readonly int[][] psts = new[] {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        }.Select(packedTable =>
        new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                    // Using positions evaled since it's an integer than initializes to zero and is assgined before being used again 
                    .Select(square => (int)((sbyte)square * 1.461) + pieceValues[positionsEvaled++ % 12])
                .ToArray()
        ).ToArray();

    // Transposition tables - stores the best move in a given position so that it can be looked up later (without having to redo a search)
    // Creating the transposition table (2^22 entries)
    private const int entries = 1 << 22; // this is 2^22
    private readonly (ulong, Move, int, int, int)[] tt = new (ulong, Move, int, int, int)[entries];

    // Killer Move array
    private readonly Move[] killerMoves = new Move[1024];

    // History Heuristics
    // Side to move, start square, end square
    private int[,,] historyHeuristics = new int[2, 64, 64];

    // Main method, finds and returns the best move in any given position
    public Move Think(Board board, Timer timer)
    {
        m_board = board;
        m_timer = timer;

        searchMaxTime = GetTimeForTurn();

        // Default move in case there is no time for any other moves
        bestIterativeMove = bestMoveRoot = m_board.GetLegalMoves()[0];
        bestIterativeEval = bestEvalRoot = 0;

        // int eval = Search(4, 0, -99999, 99999);

        // Console.WriteLine("Side: " + (m_board.IsWhiteToMove ? "White" : "Black") + "   Depth: " + 4 + "   Eval: " + eval + "   Positions Evaluated: " + positionsEvaled + "   Time: " + timer.MillisecondsElapsedThisTurn + "ms   " + bestMoveRoot);

        // If we have time to think (more than a second) then do iterative deepening, otherwise just return the first move
        if (m_timer.MillisecondsRemaining > 1000)
        {

            // Iterative deepening
            for (int depth = 1; depth <= 50; depth++)
            {
                Search(depth, 0, -99999, 99999);

                // If too much time has elapsed or a mate move has been found
                if (timer.MillisecondsElapsedThisTurn >= searchMaxTime || bestEvalRoot > 99900)
                {
                    // Console.WriteLine("Side: " + (m_board.IsWhiteToMove ? "White" : "Black") + "   Depth: " + depth + "   Eval: " + bestEvalRoot + "   Positions Evaluated: " + positionsEvaled + "   Transposition Table: " + ((double)tt.Count(s => s.bound != 0) / (double)entries * 100).ToString("F") + "%   TT values used: " + TTused + "   Time: " + timer.MillisecondsElapsedThisTurn + "ms   " + bestMoveRoot);
                    // Console.WriteLine("Side: " + (m_board.IsWhiteToMove ? "White" : "Black") + "   Depth: " + depth + "   Eval: " + bestEvalRoot + "   Time: " + timer.MillisecondsElapsedThisTurn + "ms   " + bestMoveRoot);

                    break;
                }
                else
                {
                    bestMoveRoot = bestIterativeMove;
                    bestEvalRoot = bestIterativeEval;
                }
            }
        }

        // Console.WriteLine("Max time: " + maxTime + "   time left/30: " + m_timer.MillisecondsRemaining / 30 +  "   Time used: " + m_timer.MillisecondsElapsedThisTurn + "  Used allocated time: " + (Math.Round(maxTime) == m_timer.MillisecondsElapsedThisTurn));
        // Console.WriteLine("Used allocated time: " + (Math.Round(maxTime) == m_timer.MillisecondsElapsedThisTurn));

        // Reset here so it can be used for psts unpacking when a new bot is created
        positionsEvaled = 0;
        TTused = 0;
        historyHeuristics = new int[2, 64, 64];

        return bestMoveRoot;
    }

    // Custom function which decides how long to spend on each turn based on the number of pieces remaining
    double GetTimeForTurn()
    {
        int materialCount = m_board.IsWhiteToMove ? BitboardHelper.GetNumberOfSetBits(m_board.WhitePiecesBitboard) : BitboardHelper.GetNumberOfSetBits(m_board.BlackPiecesBitboard);
        return Math.Min((-14.0625 * (materialCount - 16.4327) * (materialCount + 0.43274)), m_timer.MillisecondsRemaining / 30);
    }

    // ComPresSTO, credit to Tyrant
    int Evaluate()
    {
        int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2, piece, square;
        for (; --sideToMove >= 0; middlegame = -middlegame, endgame = -endgame)
            for (piece = -1; ++piece < 6;)
                for (ulong mask = m_board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    // Gamephase, middlegame -> endgame
                    // Multiply, then shift, then mask out 4 bits for value (0-16)
                    gamephase += 0x00042110 >> piece * 4 & 0x0F;

                    // Material and square evaluation
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middlegame += psts[square][piece];
                    endgame += psts[square][piece + 6];
                }
        // Tempo bonus
        return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (m_board.IsWhiteToMove ? 1 : -1) + gamephase / 2;
    }

    // To save tokens, Negamax and Q-Search are in a single, combined method
    int Search(int depth, int ply, int alpha, int beta)
    {
        positionsEvaled++;

        bool qSearch = depth <= 0;

        if (ply > 0)
        {
            // Detect draw by repitition
            // Returns a draw score even if this position has only appeared once in the game history (for simplicity).
            if (m_board.GameRepetitionHistory.Contains(m_board.ZobristKey))
                return 0;

            // Skip this position if a mating sequence has already been found earlier in
            // the search, which would be shorter than any mate we could find from here.
            // This is done by observing that alpha can't possibly be worse (and likewise
            // beta can't  possibly be better) than being mated in the current position.
            alpha = Math.Max(alpha, -99999 + ply);
            beta = Math.Min(beta, 99999 - ply);
            if (alpha >= beta) return alpha;
        }

        ulong zobristKey = m_board.ZobristKey;

        // Retrieve the transposition table entry (for this position, empty if it doesnt exist)
        ref var entry = ref tt[zobristKey % entries];
        Move entryMove = entry.Item2;
        int entryScore = entry.Item3;
        int entryFlag = entry.Item5;

        // Transposition Table cutoffs
        // If a position has been evaluated before (to an equal depth or higher) then just use the transposition table value
        if (ply > 0 && entry.Item1 == zobristKey && entry.Item4 >= depth && (
            entryFlag == 3 // exact score
                || entryFlag == 2 && entryScore >= beta // lower bound, fail high
                || entryFlag == 1 && entryScore <= alpha // upper bound, fail low
        ))
        {
            TTused++;
            return entryScore;
        }

        int eval;

        // Quiescence search is in the same function as negamax to save tokens
        if (qSearch)
        {
            // If in Q-search
            // A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
            // This prevents situations where a player ony has bad captures available from being evaluated as bad,
            // when the player might have good non-capture moves available.
            eval = Evaluate();
            if (eval >= beta) return beta;
            alpha = Math.Max(alpha, eval);
        }

        // Generate moves, only captures in qsearch, and order them to optimize Alpha-Beta pruning
        Move[] moves = m_board.GetLegalMoves(qSearch).OrderByDescending(
            move =>
                // Transposition Table Move
                move == entryMove ? 10_000_000 :
                // MVV-LVA (Most Valuable Victim, Least Valuable Attacker)
                move.IsCapture ? 2_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType :
                // Killer Moves
                killerMoves[depth] == move ? 1_000_000 :
                // History Heuristics
                historyHeuristics[m_board.IsWhiteToMove ? 0 : 1, move.StartSquare.Index, move.TargetSquare.Index]
        ).ToArray();

        Move bestPositionMove = Move.NullMove;
        int bestPositionEval = -99999;
        int origAlpha = alpha;

        // If there are no moves then the board is in check, which is bad, or stalemate, which is an equal position
        if (moves.Length == 0 && !qSearch)
            return m_board.IsInCheck() ? -(99999 - ply) : 0;

        foreach (Move move in moves)
        {
            // Cancel the search if we go over the time allocated for this turn
            if (m_timer.MillisecondsElapsedThisTurn >= searchMaxTime) return 99999;

            m_board.MakeMove(move);

            // Extend search by one ply if the next move is a promotion or puts the board in check
            int extension = m_board.IsInCheck() ? 1 : 0;

            eval = -Search(depth - 1 + extension, ply + 1, -beta, -alpha);
            m_board.UndoMove(move);

            // Fail-high
            if (eval >= beta)
            {
                // Move was too good, opponent will avoid this position

                // Push to TT
                entry = new(zobristKey, move, eval, depth, 2);

                // If move is quiet (non-capture)
                if (!move.IsCapture)
                {
                    // Add move to killer moves
                    killerMoves[depth] = move;

                    // Add move to history heuristic
                    historyHeuristics[m_board.IsWhiteToMove ? 0 : 1, move.StartSquare.Index, move.TargetSquare.Index] += depth * depth;
                }

                return beta;
            }

            // Found a new best move in this position
            if (eval > bestPositionEval)
            {
                bestPositionEval = eval;
                bestPositionMove = move;

                // Improve alpha
                alpha = Math.Max(alpha, bestPositionEval);

                if (ply == 0)
                {
                    bestIterativeMove = move;
                    bestIterativeEval = eval;
                }
            }
        }

        // Did we fail high/low or get an exact score?
        int bound = bestPositionEval >= beta ? 2 : bestPositionEval > origAlpha ? 3 : 1;

        // Push to TT
        // tt[key % entries] = new TTEntry(key, bestPositionMove, depth, bestPositionEval, bound);
        entry = new(
                zobristKey,
                bestPositionMove,
                bestPositionEval,
                depth,
                bound);

        return alpha;
    }
}