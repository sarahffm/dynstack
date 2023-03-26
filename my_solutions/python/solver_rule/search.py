import copy
import numpy
from pprint import pprint
from hotstorage.hotstorage_model_pb2 import World, CraneSchedule, CraneMove
from operator import attrgetter

# for calculating deposition score
DEPOSITION_WEIGHT = 0.8

# for ready score
READY_INFINITY = 999999

# to check if arrival should be cleared
ARRIVAL_UTILIZATION_LIMIT = 0.5
K = 1


class Move:
    def __init__(self, src, tgt, block):
        self.src = src
        self.tgt = tgt
        self.block = block

class Block:
    def __init__(self, id, is_ready, due):
        self.id = id
        self.due = due
        # overdue blocks aren't considered important in HS
        # consider creating an overdue attr if too many overdue blocks pollute the buffers
        self.is_ready = is_ready if self.due > 0 else False

class Stack:
    def __init__(self, id, max_height, blocks):
        self.id = id
        self.max_height = max_height
        self.blocks = blocks

    def calculate_deposition_score(self, max_due, min_due) -> float:
        height = len(self.blocks)

        if height == self.max_height:
            self.deposition_score = 0
            return self.deposition_score
        
        if height == 0:
            self.deposition_score = 1
            return self.deposition_score
        
        num_ready = sum(block.is_ready for block in self.blocks)
        average_due = sum(block.due for block in self.blocks) / height
        ready_score = 1 / (num_ready + 1)
        due_score = (average_due - min_due) / (max_due - min_due)
        self.deposition_score = DEPOSITION_WEIGHT * ready_score + (1 - DEPOSITION_WEIGHT) * due_score
        return self.deposition_score

    def calculate_ready_score(self):
        depth_count = 0
        num_ready = 0
        for i, block in enumerate(self.blocks):
            if block.is_ready:
                # blocks are sorted bottom to top but goal is to have a small value for small depth
                depth_count += len(self.blocks) + 1 - i
                num_ready += 1
        
        if num_ready == 0:
            self.ready_score = 0
            return self.ready_score
        
        # edge case: only one block is covering the ready block
        # so ready block on small stack won't be burried if there are big stacks with many burried readies
        if depth_count == 1:
            self.ready_score = READY_INFINITY
            return self.ready_score

        average_depth = depth_count / num_ready
        self.ready_score = num_ready / average_depth
        return self.ready_score

    def top(self):
        return self.blocks[-1]




############ Class definition end ############


def create_schedule(world):
    # TODO
    # - use a State object instead to store relevant information

    # initialize schedule
    schedule = CraneSchedule()

    # return if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        # case: handover wasn't ready when current relocation of ready block was scheduled
        if world.Crane.Load.Ready and world.Handover.Ready and not world.Crane.Schedule.Moves[0].TargetId == world.Handover.Id:
            print("Edge case: crane has ready block but handover wasn't ready yet when the ready block was first picked up.")
            move = CraneMove()
            move.BlockId = world.Crane.Load.Id
            move.SourceId = world.Crane.Schedule.Moves[0].SourceId
            move.TargetId = world.Handover.Id
            schedule.Moves.append(move)
            return schedule
        return None
    

    # STEP 1:
    # easy handover

    buffers = []
    for buffer in world.Buffers:
        buffers.append(Stack(buffer.Id, 
                    buffer.MaxHeight, 
                    [Block(id=block.Id, is_ready=block.Ready, due=block.Due.MilliSeconds) for block in buffer.BottomToTop]))

    for buffer in buffers:
        if len(buffer.blocks) > 0 and buffer.blocks[-1].is_ready and world.Handover.Ready:
            print("Block", buffer.blocks[-1].id, "and Handover are ready.")
            move = CraneMove()
            move.BlockId = buffer.blocks[-1].id
            move.SourceId = buffer.id
            move.TargetId = world.Handover.Id
            schedule.Moves.append(move)
            print("New schedule: ", schedule.Moves)
            # currently: return only 1 move
            return schedule


    # STEP 2:
    # arrival clearing

    max_due = 0
    min_due = 999999999
    for buffer in buffers:
        if len(buffer.blocks) > 0:
            max_temp = max(block.due for block in buffer.blocks)
            min_temp = min(block.due for block in buffer.blocks)
            if max_temp > max_due:
                max_due = max_temp
            if min_temp < min_due:
                min_due = min_temp
    print("Max due:", max_due, "\nMin due:", min_due)
            

    arrival = Stack(world.Production.Id, 
                    world.Production.MaxHeight, 
                    [Block(block.Id, block.Ready, block.Due.MilliSeconds) for block in world.Production.BottomToTop])
    print("Arrival stack contains: ", arrival.blocks)

    # check capacity of arrival stack
    free_arrival_size = arrival.max_height - len(arrival.blocks)
    print("Arrival capacity: ", free_arrival_size, "(max height", arrival.max_height, ", len:", len(arrival.blocks))

    if free_arrival_size < arrival.max_height * ARRIVAL_UTILIZATION_LIMIT + K:
        # removes 1 block from the arrival stack
        print("Removing block from arrival stack ...")

        for buffer in buffers:
            buffer.calculate_deposition_score(max_due, min_due)
        destination_stack = max(buffers, key=attrgetter('deposition_score'))

        move = CraneMove()
        move.BlockId = arrival.blocks[-1].id
        move.SourceId = arrival.id
        move.TargetId = destination_stack.id
        schedule.Moves.append(move)
        print("New schedule: ", schedule.Moves)
        return schedule
    

    # STEP 3:
    # buffer shuffling

    exists_ready_block = False
    for buffer in buffers:
        for block in buffer.blocks:
            if block.is_ready:
                exists_ready_block = True
                break
    
    if (exists_ready_block):
        # determine source & destination
        for buffer in buffers:
            buffer.calculate_ready_score()
            buffer.calculate_deposition_score(max_due, min_due)
        source_stack = max(buffers, key=attrgetter('ready_score'))
        destination_stack = max(buffers, key=attrgetter('deposition_score'))

        if destination_stack.deposition_score != 0:
            move = CraneMove()
            move = CraneMove()
            move.BlockId = source_stack.blocks[-1].id
            move.SourceId = source_stack.id
            move.TargetId = destination_stack.id
            schedule.Moves.append(move)
            print("New schedule: ", schedule.Moves)
            return schedule



    # STEP 4:
    # buffer sorting

    return None








######


def create_schedule2(world):
    print("In create_schedule function")
    
    priorities = prioritize_by_due_date(world)
    state = BrpState(world, priorities)

    # print state
    print("\nState:\n")
    state.print()
    numStacks = len(state.stacks)

    # if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        return None

    # create a random schedule
    schedule = CraneSchedule()
    
    sortedStacksByPrio = [state.stacks[0]]

    for i, stack in enumerate(state.stacks):
        if i == 0: continue

        # print("i:", i, "stackID:", stack.id)

        if (stack.blocks[-1].prio > sortedStacksByPrio[-1].blocks[-1].prio):
            sortedStacksByPrio.append(stack)

    move = False
    for stack in sortedStacksByPrio:
        print("Ready?", stack.blocks[-1].id, stack.blocks[-1].is_ready)
        if (stack.blocks[-1].is_ready and world.Handover.Ready):
            move = CraneMove()
            move.SourceId = stack.id
            move.TargetId = world.Handover.Id
            move.BlockId = stack.blocks[-1].id
            schedule.Moves.append(move)

    print("Schedule: ", schedule.Moves)

    return schedule if len(schedule.Moves) > 0 else None


def prioritize_by_due_date(world):
    # changed code to keep the original world.Production
    # all_blocks = world.Production.BottomToTop

    all_blocks = []
    all_blocks.extend(world.Production.BottomToTop)

    for stack in world.Buffers:
        all_blocks.extend(stack.BottomToTop)
    all_blocks.sort(key=lambda block: block.Due.MilliSeconds)

    return dict(zip(map(lambda block: block.Id, all_blocks), range(len(all_blocks))))

def printState(worldState):
    for stack in worldState.stacks:
        print("#### Stack", stack.id, "####")
        print("Height:", len(stack.blocks))
        for block in stack.blocks:
            print("Block", block.id, "with prio", block.prio)
        print()

class BrpState:
    def __init__(self, world, priorities):
        stacks = []
        prod = world.Production
        stacks.append(Stack(prod.Id, prod.MaxHeight, [Block(block.Id, priorities[block.Id], block.Ready) for block in reversed(prod.BottomToTop)]))

        for stack in world.Buffers:
            stacks.append(Stack(stack.Id, stack.MaxHeight, [Block(block.Id, priorities[block.Id], block.Ready) for block in stack.BottomToTop]))
        
        self.arrival_id = world.Production.Id
        self.handover_id = world.Handover.Id
        self.moves = []
        self.stacks = stacks

    def print(self):
        for stack in self.stacks:
            for block in stack.blocks:
                print("[",block.id, "/", block.prio ,end="] ")
            print("stack", stack.id)
    
    def is_solved(self):
        not any(self.not_empty_stacks())

    def not_empty_stacks(self):
        for stack in self.stacks:
            if any(stack.blocks):
                yield stack
    
    def not_full_stacks(self):
        for stack in self.stacks:
            if len(stack.blocks) < stack.max_height:
                yield stack

    def apply_move(self, move):
        result = copy.deepcopy(self)
        block = result.stacks[move.src].blocks.pop()
        if move.tgt != self.handover_id:
            result.stacks[move.tgt].blocks.append(block)
        result.moves.append(move)
        return result

    def forced_moves(self):
        moves = list()
        if not any(self.not_empty_stacks()):
            return moves
        src = min(self.not_empty_stacks(), key= lambda stack: stack.most_urgent().prio)

        urgent = src.most_urgent()
        top = src.top()
        if urgent.id == top.id:
            moves.append( Move(src.id, self.handover_id, top.id))
        else:
            for tgt in self.not_full_stacks():
                if src.id == tgt.id:
                    continue
                moves.append(Move(src.id, tgt.id, top.id))
        return moves

# old block class
"""
class Block:
    def __init__(self, id, prio, isReady, due):
        self.id = id
        self.prio = prio
        self.isReady = isReady
        self.due
"""





def crane_schedule(world):
    if len(world.Crane.Schedule.Moves) > 0:
        return None
    priorities = prioritize_by_due_date(world)
    initial = BrpState(world, priorities)
    moves = depth_first_search(initial)
    return create_schedule_from_solution(world, moves)

def create_schedule_from_solution(world, moves):
    schedule = CraneSchedule()
    handover = world.Handover
    is_ready = handover.Ready
    for opt_mov in moves[:3]:
        if not is_ready and opt_mov.tgt == handover.Id:
            break
        move = CraneMove()
        move.BlockId = opt_mov.block
        move.SourceId = opt_mov.src
        move.TargetId = opt_mov.tgt
        schedule.Moves.append(move)
    if any(schedule.Moves):
        return schedule
    else:
        return None

def depth_first_search(initial):
    budget = 1000
    best = None
    stack = [initial]
    while any(stack) and budget > 0:
        budget -= 1
        state = stack.pop()
        moves = state.forced_moves()
        if any(moves):
            for move in moves:
                stack.append(state.apply_move(move))
        else:
            sol = state.moves
            if best == None or len(sol) < len(best):
                best = sol
    return best
        
