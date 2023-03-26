from hotstorage.hotstorage_model_pb2 import World
from solver_rule import search
import CsvEditor
import re


def plan_moves(world_data):
    world = World()
    world.ParseFromString(world_data)

    file_name = './data/data_HS_test.csv'   # temporary file name
    track_kpis(world, file_name)
    
    # print("Call search.create_schedule(world)")
    crane_schedule = search.create_schedule(world)

    # print(world, crane_schedule)
    # print(crane_schedule)

    if crane_schedule:
        crane_schedule.SequenceNr = world.Crane.Schedule.SequenceNr + 1
    
    return crane_schedule


def track_kpis(world, file_name):
    kpis = dict((kpi, getattr(world.KPIs, kpi)) for kpi in dir(world.KPIs) if re.fullmatch(".*(Time|Mean|Manipulations|Blocks)", kpi)) 
    
    # if start of run: create a new data .csv file
    if str_to_ms(str(world.Now)) == 0:
        print("Creating csv ...")
        kpi_names = list(kpis.keys())
        CsvEditor.initialize_csv(file_name, kpi_names)

    # store current KPIs in csv file
    kpi_values = list(kpis.values())
    CsvEditor.addRow(file_name, [world.Now.MilliSeconds, *kpi_values])


def str_to_ms(s: str) -> int:
    if s == '': 
        return 0
    i = s.find(": ")
    return int(s[i+2:])
       




# def plan_moves(world_data, use_heuristic):
#     world = World()
#     world.ParseFromString(world_data)
#     if use_heuristic:
#         crane_schedule = heuristic.crane_schedule(world)
#     else:
#         crane_schedule = search.crane_schedule(world)
#     print(world, use_heuristic, crane_schedule)
#     # print(crane_schedule)
#     if crane_schedule:
#         crane_schedule.SequenceNr = world.Crane.Schedule.SequenceNr + 1
#     return crane_schedule