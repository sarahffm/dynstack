import copy
import numpy
from pprint import pprint
from hotstorage.hotstorage_model_pb2 import World, CraneSchedule, CraneMove
from operator import attrgetter

# for deposition score
READY_FACTOR_WEIGHT = 0.8
DUE_FACTOR_WEIGHT = 0.1
SIZE_FACTOR_WEIGHT = 0.1

# for ready score
READY_INFINITY = 999999

# to check if arrival should be cleared
ARRIVAL_UTILIZATION_LIMIT = 0.5
K = 1
MAX_BUFFER_USE = 0.9


class Move:
    def __init__(self, src, tgt, block):
        self.src = src
        self.tgt = tgt
        self.block = block

class Block:
    def __init__(self, id, is_ready, due, now):
        self.id = id
        self.due = due
        self.is_ready = is_ready
        # TODO: test overdue & implement the rest
        self.is_overdue = True if (self.due - now) <= 0 else False

class Stack:
    def __init__(self, id, max_height, blocks):
        self.id = id
        self.max_height = max_height
        self.blocks = blocks

    def calculate_deposition_score(self, max_due, min_due) -> float:
        # TODO:
        # kleine stacks mit hohem ready anteil (sehr schnell erreicht) nochmal deutlich schlechteren deposition score
        # kriegen als aktuell

        height = len(self.blocks)

        if height == self.max_height:
            self.deposition_score = 0
            return self.deposition_score
        
        if height == 0:
            self.deposition_score = 1
            return self.deposition_score
        
        num_ready = sum(block.is_ready and not block.is_overdue for block in self.blocks)
        # overdue blocks should not be added to the average due value
        average_due = sum(block.due for block in self.blocks if block.due > 0) / height

        ready_score = 1 / (num_ready + 1)
        due_score = (average_due - min_due) / (max_due - min_due)
        size_score = - (height / self.max_height) + 1
        self.deposition_score = READY_FACTOR_WEIGHT * ready_score + DUE_FACTOR_WEIGHT * due_score + SIZE_FACTOR_WEIGHT * size_score
        return self.deposition_score

    def calculate_ready_score(self):
        depth_count = 0
        num_ready = 0
        for i, block in enumerate(self.blocks):
            if block.is_ready and not block.is_overdue:
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

    def set_deposition_score(self, val):
        self.deposition_score = val

    def top(self):
        return self.blocks[-1]
    
    def find_topmost_ready_block(self):
        # blocks are sorted bottom-to-top with topmost block at [-1]
        # want to find the topmost ready block => reversed
        # stack_index is the original index, not reversed
        for stack_index, block in reversed(list(enumerate(self.blocks))):
            if block.is_ready and not block.is_overdue:
                ready_block = ReadyBlock(block.id, self, stack_index)
                return ready_block
        return False

class ReadyBlock:
    def __init__(self, id, stack, index) -> None:
        self.id = id
        self.stack = stack
        self.index = index
        self.covered = len(stack.blocks) - (index + 1)



############ Class definition end ############



def clear_arrival(arrival, buffers, min_due, max_due, schedule):
    for buffer in buffers:
        buffer.calculate_deposition_score(max_due, min_due)
    destination_stack = max(buffers, key=attrgetter('deposition_score'))
    move = create_move(block_id=arrival.top().id, source_id=arrival.id, target_id=destination_stack.id)
    schedule.Moves.append(move)

def create_move(block_id, source_id, target_id):
    move = CraneMove()
    move.BlockId = block_id
    move.SourceId = source_id
    move.TargetId = target_id
    return move


def create_schedule(world):
    # TODO:
    # - use a State object instead to store relevant information
    # - prioritize available ready block with small due date over arrival clearing
    # - emergency mode

    # initialize schedule
    schedule = CraneSchedule()

    # check if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        # case: handover wasn't ready when current relocation of ready block was scheduled
        if world.Crane.Load.Ready and world.Handover.Ready and not world.Crane.Schedule.Moves[0].TargetId == world.Handover.Id:
            # creates invalid moves (seems like block has already been handed over but this request is created again)
            move = create_move(block_id=world.Crane.Load.Id, source_id=world.Crane.Schedule.Moves[0].TargetId, target_id=world.Handover.Id)
            schedule.Moves.append(move)
            print("Edge case has schedule new schedule: ", schedule.Moves)
            return schedule
        return None
    

    # STEP 1:
    # easy handover

    buffers = []
    for buffer in world.Buffers:
        buffers.append(Stack(buffer.Id, 
                    buffer.MaxHeight, 
                    [Block(id=block.Id, is_ready=block.Ready, due=block.Due.MilliSeconds, now=world.Now.MilliSeconds) for block in buffer.BottomToTop]))

    print_if_invalid(world, buffers)

    for buffer in buffers:
        if len(buffer.blocks) > 0 and buffer.top().is_ready and not buffer.top().is_overdue and world.Handover.Ready:
            move = create_move(block_id=buffer.top().id, source_id=buffer.id, target_id=world.Handover.Id)
            schedule.Moves.append(move)
            print("Easy handover new schedule: ", schedule.Moves)
            return schedule



    # STEP 2:
    # arrival clearing

    # IDEA: delay arrival clearing if: handover-estimation nur kurze Zeit bis ready
    #       and production interval relativ groß

    used_buffer_capacity = 0
    total_buffer_capacity = len(buffers) * buffers[0].max_height

    max_due = 0
    min_due = 999999999

    for buffer in buffers:
        size = len(buffer.blocks)
        used_buffer_capacity += size
        if size > 0:
            max_temp = max(block.due for block in buffer.blocks)
            min_temp = min(block.due for block in buffer.blocks)
            if max_temp > max_due:
                max_due = max_temp
            if min_temp < min_due:
                min_due = min_temp
    
    buffer_use = used_buffer_capacity / total_buffer_capacity
    print("Buffer capacity", buffer_use,"%. Total:", total_buffer_capacity, "Used:", used_buffer_capacity)
    EMERGENCY_MODE = True if buffer_use > MAX_BUFFER_USE else False

    arrival = Stack(world.Production.Id, 
                    world.Production.MaxHeight, 
                    [Block(block.Id, block.Ready, block.Due.MilliSeconds, now=world.Now.MilliSeconds) for block in world.Production.BottomToTop])

    # check capacity of arrival stack
    free_arrival_size = arrival.max_height - len(arrival.blocks)

    if EMERGENCY_MODE:
        print("EMERGENCY MODE:", EMERGENCY_MODE)
        # buffer shuffling
        # TODO: 
        # - if locked, move overdue to (ready) handover
        # - find reachable ready block
        #   - reachable if equal or less blocks on top than are free in total

        # find topmost ready blocks
        ready_blocks = []
        for buffer in buffers:
            ready_block = buffer.find_topmost_ready_block()
            if ready_block:
                ready_blocks.append(ready_block)
        
        #print("Ready blocks:", ready_blocks)
        
        # if ready block found
        if ready_blocks:
            print("Ready block exists")
            # choose best ready block
            ready_blocks_sorted = sorted(ready_blocks, key=lambda ready_block: ready_block.covered)
            topmost_ready = ready_blocks_sorted[0]
            print("Best ready block found:", topmost_ready.id, topmost_ready.covered)

            # check if ready block reachable
            free_capacity = total_buffer_capacity - used_buffer_capacity
            chosen_stack_capacity = topmost_ready.stack.max_height - len(topmost_ready.stack.blocks)

            if topmost_ready.covered <= (free_capacity - chosen_stack_capacity):
                print("Ready block is reachable!")
                # source is stack withe the best ready block
                source_stack = topmost_ready.stack

                # determine destination stack

                if source_stack.top().is_overdue and world.Handover.Ready:
                    # move covering block to handover if possible
                    move = create_move(block_id=source_stack.top().id, source_id=source_stack.id, target_id=world.Handover.Id)
                    schedule.Moves.append(move)
                    print("Emergency shuffling to handover new schedule: ", schedule.Moves)
                    return schedule
                
                for buffer in buffers:
                    # move covering block to any non-full stack
                    buffer.calculate_deposition_score(max_due, min_due)
                    if buffer.id != source_stack.id and buffer.deposition_score != 0:
                        # destination has been found. Create a new move
                        destination_stack = buffer
                        move = create_move(block_id=source_stack.top().id, source_id=source_stack.id, target_id=destination_stack.id)
                        schedule.Moves.append(move)
                        print("Emergency shuffling to other stack new schedule: ", schedule.Moves)
                        return schedule
        
        # clear over_dues if there is nothing else to clear or shuffle
        if world.Handover.Ready:
            for buffer in buffers:
                if buffer.top().is_overdue:
                    move = create_move(block_id=buffer.top().id, source_id=buffer.id, target_id=world.Handover.Id)
                    schedule.Moves.append(move)
                    print("Clear overdue new schedule: ", schedule.Moves)
                    return schedule

        print("Wait...")
        return None
        """# emergency low priority arrival clearing
        if free_arrival_size == 0:
            clear_arrival(arrival, buffers, min_due, max_due, schedule)
            print("Full arrival clearing new schedule: ", schedule.Moves)
            return schedule"""


    # normal arrival clearing
    if free_arrival_size <= arrival.max_height * ARRIVAL_UTILIZATION_LIMIT + K:
        clear_arrival(arrival, buffers, min_due, max_due, schedule)
        print("Normal arrival clearing new schedule: ", schedule.Moves)
        return schedule
    

    # STEP 3:
    # normal buffer shuffling

    exists_ready_block = False
    for buffer in buffers:
        for block in buffer.blocks:
            if block.is_ready and not block.is_overdue:
                exists_ready_block = True
                break
    
    if (exists_ready_block):
        # determine source & destination
        for buffer in buffers:
            buffer.calculate_ready_score()
            buffer.calculate_deposition_score(max_due, min_due)

        source_stack = max(buffers, key=attrgetter('ready_score'))

        # destination: handover if block is overdue and handover is ready
        if source_stack.top().is_overdue and world.Handover.Ready:
            print("Overdue block to handover ...")
            destination_stack = Stack(world.Handover.Id, 1, [])
            destination_stack.set_deposition_score(1)
        else:
            # destination: stack with best deposition score if block is not overdue
            destination_stack = max(buffers, key=attrgetter('deposition_score'))

        # case: source & destination are same stack
        if source_stack.id == destination_stack.id:
            print("Buffer shuffling case source == destination")
            # ready score more important than deposition score => choose "second best" destination
            buffers_sorted = sorted(buffers, key=lambda buffer: buffer.deposition_score, reverse=True)
            destination_stack = buffers_sorted[1]
            print("New destination id:", destination_stack.id)

        # create move if chosen destination is not full
        if destination_stack.deposition_score != 0:
            move = create_move(block_id=source_stack.top().id, source_id=source_stack.id, target_id=destination_stack.id)
            schedule.Moves.append(move)
            print("Buffer shuffling new schedule: ", schedule.Moves)
            return schedule
    
    

    # STEP 4:
    # buffer sorting
    print("TODO buffer sorting ...")

    return None


# for debugging
def print_if_invalid(world, buffers):
    if world.InvalidMoves:
        print("INVALID MOVE\n", world.InvalidMoves, "\n")
        print("STATE NOW:")
        for stack in buffers:
            for block in stack.blocks:
                print("[",block.id, "/" ,end="] ")
            print("stack", stack.id)
        print("\n\n")








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
        
