import copy
import numpy
from pprint import pprint
from hotstorage.hotstorage_model_pb2 import World, CraneSchedule, CraneMove


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

class Move:
    def __init__(self, src, tgt, block):
        self.src = src
        self.tgt = tgt
        self.block = block

class Block:
    def __init__(self, id, prio, isReady):
        self.id = id
        self.prio = prio
        self.isReady = isReady

class Stack:
    def __init__(self, id, max_height, blocks):
        self.id = id
        self.max_height = max_height
        self.blocks = blocks

    def top(self):
        return self.blocks[-1]

    def most_urgent(self):
        return min(self.blocks, key=lambda block: block.prio)

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


def create_schedule(world):
    print(world)

def create_schedule2(world):
    print("in create_schedule function")
    

    priorities = prioritize_by_due_date(world)
    state = BrpState(world, priorities)

    # print state
    print("\nState:\n")
    state.print()
    numStacks = len(state.stacks)
    """
    print("Number of stacks:", numStacks, "\n")
    print("Production:")
    print(world.Production)
    print("Handover:")
    print(world.Handover)
    """


    # if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        return None

    # create a random schedule
    schedule = CraneSchedule()
    
    sortedStacksByPrio = [state.stacks[0]]

    for i, stack in enumerate(state.stacks):
        if i == 0: continue

        print("i:", i, "stackID:", stack.id)

        if (stack.blocks[-1].prio > sortedStacksByPrio[-1].blocks[-1].prio):
            sortedStacksByPrio.append(stack)

    move = False
    for stack in sortedStacksByPrio:
        print("Ready?", stack.blocks[-1].id, stack.blocks[-1].isReady)
        if (stack.blocks[-1].isReady and world.Handover.Ready):
            move = CraneMove()
            move.SourceId = stack.id
            move.TargetId = world.Handover.Id
            move.BlockId = stack.blocks[-1].id
            schedule.Moves.append(move)

    print("Schedule: ", schedule.Moves)

    return schedule if len(schedule.Moves) > 0 else None


"""
print("world:")
pprint(dir(world))

print("state:")
pprint(dir(initialState))
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
        




