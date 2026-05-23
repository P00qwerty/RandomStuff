using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgOpenGPS;

namespace AgOpenGPS2
{
    #region COMMANDS

    [Flags]
    public enum FieldCommand
    {
        None = 0,
        CalculateFenceLine = 1,
        CalculateTurnLine = 2,
        CalculateBufferLine = 4,
        CalculateHeadLine = 8,
    }
    #endregion


    public class BoundaryLines
    {
        public Polyline fenceLine = new Polyline();

        public Polyline shoulderLine = new Polyline();
        public List<Segment> segments = new List<Segment>();

        public Polyline bufferLine = new Polyline();

        public Polyline turnLine = new Polyline();
        public List<Polyline> hdLine = new List<Polyline>();

        public bool isOuter = true;
        public bool isDriveThru = true;

        public BoundaryLines()
        {
        }
    }

    #region FIELD

    public class CField : LTP
    {
        public BoundingBox LatLonBox;
        public double AreaLessInner = -0.0001;
        public string dir = "";
        public string name = "";
        public bool Selected = false;
        public bool DriveIn = false;

        //list of coordinates of boundary line
        public List<CBoundaryList> boundaries = new List<CBoundaryList>();

        public List<Polyline> headLines = new List<Polyline>();
        public bool isStaticHeadland = false;

        // Processor
        private FieldProcessor Processor { get; }
        private readonly Task _processorTask;

        public CField()
        {
            Processor = new FieldProcessor(this);

            // Start background worker
            _processorTask = Task.Run(Processor.ProcessLoopAsync);
        }

        // Public API

        public void CalculateFenceLine(int index = -1)
        {
            Processor.Enqueue(FieldCommand.CalculateFenceLine | FieldCommand.CalculateTurnLine | FieldCommand.CalculateBufferLine | FieldCommand.CalculateHeadLine, index);
        }

        public void CalculateTurnLine(int index = -1)
        {
            Processor.Enqueue(FieldCommand.CalculateTurnLine, index);
        }

        public void CalculateBufferLine(int index = -1)
        {
            Processor.Enqueue(FieldCommand.CalculateBufferLine | FieldCommand.CalculateHeadLine, index);
        }

        public void CalculateHeadLine(int index = -1)
        {
            Processor.Enqueue(FieldCommand.CalculateHeadLine, index);
        }

        public void SaveField()
        {
            //AgOpenGPS.AsyncSaveField(field);
            //Processor.Enqueue(FieldCommand.CalculateHeadLine, -1);
        }
    }

    #endregion

    #region PROCESSOR

    public class FieldProcessor
    {
        private readonly CField _field;

        private readonly Channel<FieldCommand> _channel = Channel.CreateUnbounded<FieldCommand>();

        public FieldProcessor(CField field)
        {
            _field = field;
        }

        public void Enqueue(FieldCommand command, int index = -1)
        {
            _channel.Writer.TryWrite(command);
        }

        public async Task ProcessLoopAsync()
        {
            try
            {
                await foreach (var command in _channel.Reader.ReadAllAsync())
                {
                    FieldCommand commands = command;

                    while (_channel.Reader.TryRead(out var newer))
                    {
                        commands |= newer;
                    }

                    if (_field.boundaries == null || _field.boundaries.Count == 0)
                        continue;

                    if (commands.HasFlag(FieldCommand.CalculateFenceLine))
                    {
                        ProcessFenceLines();
                    }

                    if (commands.HasFlag(FieldCommand.CalculateTurnLine))
                    {
                        ProcessTurnLine();
                    }

                    if (commands.HasFlag(FieldCommand.CalculateBufferLine))
                    {
                        ProcessBufferLine();
                    }

                    if (commands.HasFlag(FieldCommand.CalculateHeadLine))
                    {
                        ProcessHeadline();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                glm.WriteErrorLog(ex);
            }
        }

        // =====================================================
        // BOUNDARY
        // =====================================================

        private void ProcessFenceLines()
        {
            for (int i = 0; i < _field.boundaries.Count; i++)
            {
                CalculateFenceLine(_field.boundaries[i].fenceLine);

                if (_field.boundaries[i].shoulderLine.points.Count > 0)
                {
                    CalculateFenceLine(_field.boundaries[i].shoulderLine);
                }
                else
                {
                    _field.boundaries[i].shoulderLine = _field.boundaries[i].fenceLine;
                }
            }
        }

        // =====================================================
        // TurnLine
        // =====================================================

        private void ProcessTurnLine()
        {
            for (int i = 0; i < _field.boundaries.Count; i++)
            {
                _field.boundaries[i].turnLine = _field.boundaries[i].shoulderLine.Offset<Polyline>(CancellationToken.None, Settings.Tool.UTurnDistance, i == 0, Settings.Vehicle.MinTurnRadius)[0];

                _field.boundaries[i].turnLine.PreCalc();
            }
        }

        private void ProcessBufferLine()
        {
            for (int i = 0; i < _field.boundaries.Count; i++)
            {
                var whatToUse = Settings.Tool.ShoulderType > 0 ? _field.boundaries[i].shoulderLine : _field.boundaries[i].fenceLine;

                if (Settings.Tool.ShoulderType == 2)
                {
                    _field.boundaries[i].bufferLine = whatToUse.CreateBufferOffset(_field.boundaries[i].segments, i == 0);
                    _field.boundaries[i].bufferLine.PreCalc();
                }
                else
                    _field.boundaries[i].bufferLine = whatToUse;
            }
        }

        // =====================================================
        // HEADLINE
        // =====================================================

        private void ProcessHeadline()
        {
            if (_field.boundaries == null || _field.boundaries.Count == 0)
                return;

            if (!_field.isStaticHeadland && _field.boundaries.Count > 0)
            {
                //if (force || field.boundaries[i].hdLine.Count == 0 || field.boundaries[i].shoulderLine != field.boundaries[i].fenceLine)
                if (_field.headLines.Count == 0 || _field.boundaries[0].shoulderLine != _field.boundaries[0].fenceLine)//|| force)
                {
                    var outerHeadLines = _field.boundaries[0].bufferLine.Offset<Polyline>(CancellationToken.None, Settings.Tool.DefaultHeadlandWidth, true);

                    for (int i = 1; i < _field.boundaries.Count; i++)
                    {
                        var innerHeadLines = _field.boundaries[i].bufferLine.Offset<Polyline>(CancellationToken.None, Settings.Tool.DefaultHeadlandWidth, false);

                        for (int j = 0; j < outerHeadLines.Count; j++)
                        {
                            for (int k = 0; k < innerHeadLines.Count; k++)
                            {
                                if (StaticClass.GeneralPolygonClippping(outerHeadLines[j].points, innerHeadLines[k].points, ClipType.Difference, out var clippedPolygon))
                                {
                                    outerHeadLines[j].points = clippedPolygon;
                                }
                            }
                        }
                    }

                    for (int j = 0; j < outerHeadLines.Count; j++)
                    {
                        outerHeadLines[j].PreCalc();
                    }
                    _field.headLines = outerHeadLines;//clipped with inner!
                }
            }
        }

        // =====================================================
        // COMPUTE METHODS
        // =====================================================

        private Polyline CalculateFenceLine(Polyline poly)
        {
            //make sure fenceline is clockwise
            bool clockwise = poly.points.IsClockwise(true, out double area);

            double tolerance;
            //boundary point spacing based on eq width
            //close if less then 30 ha, 60ha, more then 60
            if (area < 300000) tolerance = 0.01;
            else if (area < 600000) tolerance = 0.05;
            else tolerance = 0.1;

            poly.points.LangSimplify(tolerance);

            //if (!isOuter)//make sure fenceline lays in the main fenceline?
            {
                //force the points to this fenceline?
                //StaticClass.CalculateIntersectionArea(bndList[0].fenceLine.points, bndList[i].fenceLine.points);//should also make it force inside outer!
            }

            poly.loop = true;

            //no idea why this fixes some strange bugs
            //fenceLine = fenceLine.Offset<Polyline>(CancellationToken.None, 0.0, isOuter, 0.01)[0];

            poly.PreCalc();

            return poly;
        }
    }

    #endregion
}