from hotstorage.hotstorage_model_pb2 import World
from solver_rule import search
import CsvEditor
import re


def plan_moves(world_data, file_name):
    world = World()
    world.ParseFromString(world_data)

    # prepare KPIs
    kpis = dict((kpi, getattr(world.KPIs, kpi)) for kpi in dir(world.KPIs) if re.fullmatch(".*(Time|Mean|Manipulations|Blocks)", kpi)) 
    print("KPIS:", kpis)
    kpi_values = list(kpis.values())
    print("KPI values only:", kpi_values)

    # store current KPIs in csv file
    CsvEditor.addRow(file_name, [str_to_ms(str(world.Now)), *kpi_values])
    
    print("call search.create_schedule(world)")
    crane_schedule = search.create_schedule(world)

    # print(world, crane_schedule)
    # print(crane_schedule)

    if crane_schedule:
        crane_schedule.SequenceNr = world.Crane.Schedule.SequenceNr + 1
    
    return crane_schedule


def str_to_ms(s: str) -> int:
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