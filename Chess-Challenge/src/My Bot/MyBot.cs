using System;
using System.Linq;
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
    static int[] pieceValues = { 77, 302, 310, 434, 890, 0, // Middlegame
                                 109, 331, 335, 594, 1116, 0, }; // Endgame;
    int maxDepth,
        millisRemaining;
    bool searchCanceled;
    Timer timer;

    //Item1 is hash; Item2 is eval; Item3 is depth; Item4 is tMaxDepth; Item5 is flag
    (ulong, int, int, int, int)[] TTable = new (ulong, int, int, int, int)[0x7FFFFF + 1];

    int[] UnpackedPestoTables = 
        new[] {
            59445390105436474986072674560m, 70290677894333901267150682880m, 71539517137735599738519086336m, 78957476706409475571971323392m, 76477941479143404670656189696m, 78020492916263816717520067072m, 77059410983631195892660944640m, 61307098105356489251813834752m,
            77373759864583735626648317994m, 3437103645554060776222818613m, 5013542988189698109836108074m, 2865258213628105516468149820m, 5661498819074815745865228343m, 8414185094009835055136457260m, 7780689186187929908113377023m, 2486769613674807657298071274m,
            934589548775805732457284597m, 4354645360213341838043912961m, 8408178448912173986754536726m, 9647317858599793704577609753m, 9972476475626052485400971547m, 9023455558428990305557695533m, 9302688995903440861301845277m, 4030554014361651745759368192m,
            78006037809249804099646260205m, 5608292212701744542498884606m, 9021118043939758059554412800m, 11825811962956083217393723906m, 11837863313235587677091076880m, 11207998775238414808093699594m, 9337766883211775102593666830m, 4676129865778184699670239740m,
            75532551896838498151443462373m, 3131203134016898079077499641m, 8090231125077317934436125943m, 11205623443703685966919568899m, 11509049675918088175762150403m, 9025911301112313205746176509m, 6534267870125294841726636036m, 3120251651824756925472439792m,
            74280085839011331528989207781m, 324048954150360030097570806m, 4681017700776466875968718582m, 7150867317927305549636569078m, 7155688890998399537110584833m, 5600986637454890754120354040m, 1563108101768245091211217423m, 78303310575846526174794479097m,
            70256775951642154667751105509m, 76139418398446961904222530552m, 78919952506429230065925355250m, 2485617727604605227028709358m, 3105768375617668305352130555m, 1225874429600076432248013062m, 76410151742261424234463229975m, 72367527118297610444645922550m,
            64062225663112462441888793856m, 67159522168020586196575185664m, 71185268483909686702087266048m, 75814236297773358797609495296m, 69944882517184684696171572480m, 74895414840161820695659345152m, 69305332238573146615004392448m, 63422661310571918454614119936m,
        }.SelectMany(packedTable => decimal
               .GetBits(packedTable).SelectMany(BitConverter.GetBytes)
               .Select((square, index) => (int)((sbyte)square * 1.461) + pieceValues[index % 12])
               .ToArray())
               .ToArray();


public Move Think(Board b, Timer t)
    {
        board = b;
        bestMove = SortMoves(board.GetLegalMoves())[0];
        timer = t;
        searchCanceled = false;
        maxDepth = 1;
        millisRemaining = t.MillisecondsRemaining;

        //DEBUG
        Console.WriteLine();

        for (; ;)
        {
            //DEBUG
            /*
                int eval = Negamax(++maxDepth, -10000, 10000);

                string evalStr = eval.ToString();
                string sign = (Math.Abs(eval) >= 9980) ? (Math.Sign(eval) == 1 ? "+" : "-") : "";

                if (Math.Abs(eval) >= 9980) evalStr = "MATE IN " + Math.Floor(((double)(10000 - Math.Abs(eval)) / 2) + 1).ToString();

                Console.WriteLine("M: " + sign + evalStr
                                  + "; d = " + maxDepth
                                  + "; Best " + bestMove
                                  + "; in " + timer.MillisecondsElapsedThisTurn + "ms");

                if (searchCanceled || eval >= 9980) break;
            */            

            //Optimized version:
            if (Negamax(++maxDepth, -10000, 10000) >= 9980 || searchCanceled) break;
        }

        return bestMove;
    }

    int Negamax(int depth, int alpha, int beta)
    {
        //Bunch of conditionals at the beginning to save computational time
        if (timer.MillisecondsElapsedThisTurn
             >= millisRemaining / 30
             || maxDepth >= 100) searchCanceled = true;
        if (searchCanceled) return 0;
        if (board.IsDraw()) return -20;
        if (board.IsInCheckmate()) return -10000 + (maxDepth - depth);
        if (depth == 0) return QSearch(alpha, beta);

        //Item1 is hash; Item2 is eval; Item3 is depth; Item4 is tMaxDepth; Item5 is flag
        ref (ulong, int, int, int, int) transposition = ref TTable[board.ZobristKey & 0x7FFFFF];

        int TE = transposition.Item2, 
            eval,
            bestEval = -10000,
            startingAlpha = alpha,
            flaggie = transposition.Item5,
            setFlag;
        
        if (depth > 2
            && transposition.Item1 == board.ZobristKey
            && transposition.Item3 <= depth
            && transposition.Item4 > maxDepth
            &&
            (flaggie == 1
            || (flaggie == 2 && TE > beta)
            || (flaggie == 3 && TE <= alpha)))
                return TE;

        foreach (Move response in SortMoves(board.GetLegalMoves()))
        {
            board.MakeMove(response);
            eval = -Negamax(depth - 1, -beta, -alpha);
            board.UndoMove(response);

            if (eval > bestEval && !searchCanceled)
            {
                bestEval = eval;
                if (depth == maxDepth) bestMove = response;
            }

            if (eval >= beta) return beta;

            alpha = Math.Max(alpha, eval);
        }

        if (!searchCanceled)
        {
            if (bestEval < startingAlpha)
                setFlag = 3; //upper bound

            else if (bestEval >= beta)
                setFlag = 2; //lower bound

            else setFlag = 1; //"exact" score

            transposition = (board.ZobristKey, bestEval, depth, maxDepth, setFlag);
        }

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

    //Thx Tyrant
    int Evaluate()
    {
        int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2, piece, square;
        for (; --sideToMove >= 0; middlegame = -middlegame, endgame = -endgame)
            for (piece = 6; --piece >= 0;)
                for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    // Gamephase, middlegame -> endgame
                    // Multiply, then shift, then mask out 4 bits for value (0-16)
                    gamephase += 0x00042110 >> piece * 4 & 0x0F;

                    // Material and square evaluation
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middlegame += UnpackedPestoTables[square * 16 + piece];
                    endgame += UnpackedPestoTables[square * 16 + piece + 6];

                    // Bishop pair bonus
                    if (piece == 2 && mask != 0)
                    {
                        middlegame += 23;
                        endgame += 62;
                    }

                    // Doubled pawns penalty (brought to my attention by Y3737)
                    if (piece == 0 && (0x101010101010101UL << (square & 7) & mask) > 0)
                    {
                        middlegame -= 15;
                        endgame -= 15;
                    }
                }
        return (middlegame * gamephase + endgame * (24 - gamephase)) / (board.IsWhiteToMove ? 24 : -24)
            // Tempo bonus to help with aspiration windows
            + 16;
    }

    struct SortableMove
    {
        public Move UnsortedMove;
        public int Ranking;
    }

    //set element number so you dont have to initialize the array every time
    SortableMove[] sortableMoves = new SortableMove[100];

    Move[] SortMoves(Move[] movesToSort)
    {
        int i = -1;

        //Don't use array.clear because you only use the part of the array that you modify

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

            //modify
            sortableMoves[++i].Ranking = moveScoreGuess;
            sortableMoves[i].UnsortedMove = move;
        }


        //Only use the array that we modified, then sort
        //Convert all elements to the move element

        var ourMoves = sortableMoves.Take(movesToSort.Length).ToArray();
        Array.Sort(ourMoves, (x, y) => x.Ranking.CompareTo(y.Ranking));

        return ourMoves.Select(a => a.UnsortedMove).ToArray();
    }
}
