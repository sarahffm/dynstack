using DynStack.DataModel.HS;

var settings = new Settings{
    ProductionMaxHeight = 4,
    BufferMaxHeight = 8,
    BufferCount = 6,
    SimulationDuration = new TimeSpan(0, 2, 0),
    CheckInterval = new TimeSpan(0, 0, 0.5),
    MinClearTime = new TimeSpan(0, 0, 2),
    MaxClearTime = new TimeSpan(0, 0, 4),
    CraneMoveTimeMean = new TimeSpan(0, 0, 4),
    CraneMoveTimeStd = new TimeSpan(0, 0, 1),
    HoistMoveTimeMean = new TimeSpan(0, 0, 2),
    DueTimeMean = new TimeSpan(0, 0, 0.5),
    DueTimeMean = new TimeSpan(0, 26, 43),
    DueTimeStd = new TimeSpan(0, 5, 0),
    DueTimeMin = new TimeSpan(0, 1, 0),
    Seed = 13,
    ReadyFactorMin = 0.65,
    ReadyFactorMax = 0.85,
    ArrivalTimeMean = new TimeSpan(0, 0, 35.77),
    ArrivalTimeStd = new TimeSpan(0, 0, 8),
    HandoverTimeMean = new TimeSpan(0, 0, 2),
    HandoverTimeStd = new TimeSpan(0, 0, 0.5),
    InitialNumberOfBlocks = 34
};

using (var fileStream = File.Create("settings1.buf"))
{
    Serializer.Serialize(fileStream, settings);
}
