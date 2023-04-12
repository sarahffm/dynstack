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
K = 0
MAX_BUFFER_USE = 0.9



class Block:
    def __init__(self, id, is_ready, due, now):
        self.id = id
        self.due = due
        self.is_ready = is_ready
        self.is_overdue = True if (self.due - now) <= 0 else False

class Stack:
    def __init__(self, id, max_height, blocks):
        self.id = id
        self.max_height = max_height
        self.blocks = blocks

    def calculate_deposition_score(self, max_due, min_due) -> float:
        # TODO:
        # kleine stacks mit hohem ready anteil (sehr schnell erreicht) nochmal deutlich schlechteren deposition score

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
        # so ready block on small stack won't be buried if there are big stacks with many buried readies
        if depth_count == 1:
            self.ready_score = READY_INFINITY
            return self.ready_score

        average_depth = depth_count / num_ready
        self.ready_score = num_ready / average_depth
        return self.ready_score

    def top(self):
        return self.blocks[-1]
    
    def find_topmost_ready_block(self):
        # blocks are sorted bottom-to-top with topmost block at [-1]
        # want to find the topmost ready block => reversed
        # stack_index is the original index, not reversed
        for stack_index, block in reversed(list(enumerate(self.blocks))):
            if block.is_ready and not block.is_overdue:
                return PositionedBlock(block.id, self, stack_index)
        return False
    
    def find_topmost_overdue_block(self):
        for stack_index, block in reversed(list(enumerate(self.blocks))):
            if block.is_overdue:
                return PositionedBlock(block.id, self, stack_index)
        return False

class Handover:
    def __init__(self, id, is_ready) -> None:
        self.id = id
        self.is_ready = is_ready
        self.deposition_score = 1

class PositionedBlock:
    def __init__(self, id, stack, index) -> None:
        self.id = id
        self.stack = stack
        self.index = index
        self.covered = len(stack.blocks) - (index + 1)

class WorldState:
    def __init__(self, world) -> None:
        self.arrival = self.create_arrival(world)
        self.buffers = self.create_buffers(world)
        self.handover = Handover(world.Handover.Id, world.Handover.Ready)

    def create_buffers(self, world):
        buffers = []
        for buffer in world.Buffers:
            buffers.append(Stack(buffer.Id, 
                        buffer.MaxHeight, 
                        [Block(id=block.Id, is_ready=block.Ready, due=block.Due.MilliSeconds, now=world.Now.MilliSeconds) for block in buffer.BottomToTop]))
        return buffers
    
    def create_arrival(self, world) -> Stack:
        arrival = Stack(world.Production.Id, 
                    world.Production.MaxHeight, 
                    [Block(block.Id, block.Ready, block.Due.MilliSeconds, now=world.Now.MilliSeconds) for block in world.Production.BottomToTop])
        return arrival
    
    def initialize_buffer_scores(self, max_due, min_due):
        for buffer in self.buffers:
            buffer.calculate_ready_score()
            buffer.calculate_deposition_score(max_due, min_due)
    
    def has_ready_blocks(self):
        exists_ready_block = False
        for buffer in self.buffers:
            for block in buffer.blocks:
                if block.is_ready and not block.is_overdue:
                    exists_ready_block = True
                    break
        return exists_ready_block

    def try_easy_handover(self):
        move = False
        for buffer in self.buffers:
            if len(buffer.blocks) > 0 and buffer.top().is_ready and not buffer.top().is_overdue and self.handover.is_ready:
                move = self.deliver_block(buffer)
        return move
    
    def deliver_block(self, source_stack):
        return create_move(block_id=source_stack.top().id, source_id=source_stack.id, target_id=self.handover.id)

    def clear_arrival(self, min_due, max_due, schedule):
        for buffer in self.buffers:
            buffer.calculate_deposition_score(max_due, min_due)
        
        destination_stack = max(self.buffers, key=attrgetter('deposition_score'))

        move = create_move(block_id=self.arrival.top().id, source_id=self.arrival.id, target_id=destination_stack.id)
        schedule.Moves.append(move)
    
    def choose_source(self):
        return max(self.buffers, key=attrgetter('ready_score'))

    def choose_destination(self, source_stack):
        if source_stack.top().is_overdue and self.handover.is_ready:
            # print("Overdue block to handover ...")
            destination_stack = self.handover
        else:
            # destination: stack with best deposition score if block is not overdue
            destination_stack = max(self.buffers, key=attrgetter('deposition_score'))
        
        # special case: source & destination are same stack
        if source_stack.id == destination_stack.id:
            # print("Buffer shuffling case source == destination")
            # choose new destination
            destination_stack = self.choose_next_best_destination()
        
        return destination_stack
    
    def choose_next_best_destination(self):
        # ready score more important than deposition score => choose "second best" destination
        buffers_sorted = sorted(self.buffers, key=lambda buffer: buffer.deposition_score, reverse=True)
        destination_stack = buffers_sorted[1]
        print("New destination id:", destination_stack.id)
        
        return destination_stack
    
    def is_full_mode(self, buffer_use):
        # condition 1:
        if buffer_use > MAX_BUFFER_USE:
            return True

        # condition 2: if only 1 buffer has free capacity
        num_full_stacks = 0
        for buffer in self.buffers:
            if buffer.max_height == len(buffer.blocks):
                num_full_stacks += 1
        if num_full_stacks == len(self.buffers) - 1:
            return True
        return False

    def get_topmost_ready_blocks(self):
        ready_blocks = []
        for buffer in self.buffers:
            ready_block = buffer.find_topmost_ready_block()
            if ready_block:
                ready_blocks.append(ready_block)
        return ready_blocks
    
    def get_topmost_overdue_blocks(self):
        overdue_blocks = []
        for buffer in self.buffers:
            overdue_block = buffer.find_topmost_overdue_block()
            if overdue_block:
                overdue_blocks.append(overdue_block)
        return overdue_blocks
        
    def try_get_reachable_block(self, ready_blocks, total_buffer_capacity, used_buffer_capacity, max_due, min_due):
        ready_blocks_sorted = sorted(ready_blocks, key=lambda ready_block: ready_block.covered)
        free_capacity = total_buffer_capacity - used_buffer_capacity

        # for each ready block, check if it's reachable and if so, start digging it out
        for i, topmost_ready in enumerate(ready_blocks_sorted):
            topmost_ready = ready_blocks_sorted[i]
            # print("Best ready/overdue block found:", topmost_ready.id, topmost_ready.covered)

            source_stack_capacity = topmost_ready.stack.max_height - len(topmost_ready.stack.blocks)

            # filter out full buffers
            free_buffers = [buffer for buffer in self.buffers if buffer.max_height - len(buffer.blocks) > 0]
            # sort remaining buffers by free capacity (descending)
            free_buffers_sorted = sorted(free_buffers, key=lambda buffer: (buffer.max_height - len(buffer.blocks)), reverse=True)

            if topmost_ready.covered <= (free_capacity - source_stack_capacity):
                # print("Ready/overdue block is reachable!")
                source_stack = topmost_ready.stack
                destination_stack = self.try_find_destination(source_stack, free_buffers_sorted, max_due, min_due)
                
                # move to handover or non-full stack if destination found
                if destination_stack:
                    move = create_move(source_stack.top().id, source_stack.id, destination_stack.id)
                    return move
        
        # print("No reachable block ...")
        return False
    
    def try_find_destination(self, source_stack, free_buffers_sorted, max_due, min_due):
        destination_stack = False

        if source_stack.top().is_overdue and self.handover.is_ready:
            # move covering block to handover if possible
            destination_stack = self.handover
            # print("Emergency shuffling to handover")
        
        for buffer in free_buffers_sorted:
            # move covering block to any non-full stack
            buffer.calculate_deposition_score(max_due, min_due)
            if buffer.id != source_stack.id and buffer.deposition_score != 0:
                destination_stack = buffer
                # print("Emergency shuffling to other stack.")
        
        return destination_stack

    def try_clear_accessible_overdues(self):
        move = False
        for buffer in self.buffers:
            if buffer.top().is_overdue:
                move = self.deliver_block(buffer)
        return move




def create_move(block_id, source_id, target_id):
    move = CraneMove()
    move.BlockId = block_id
    move.SourceId = source_id
    move.TargetId = target_id
    return move


def create_schedule(world):
    # TODO: prioritize available ready block with small due date over arrival clearing

    # initialize world state object
    state = WorldState(world)

    # initialize schedule
    schedule = CraneSchedule()

    # print for debugging:
    print_if_invalid(world, state.buffers)

    # check if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        # case: handover wasn't ready when current relocation of ready block was scheduled
        if world.Crane.Load.Ready and state.handover.is_ready and not world.Crane.Schedule.Moves[0].TargetId == state.handover.id:
            # creates invalid moves (seems like block has already been handed over but this request is created again)
            move = create_move(block_id=world.Crane.Load.Id, source_id=world.Crane.Schedule.Moves[0].TargetId, target_id=state.handover.id)
            schedule.Moves.append(move)
            # print("Edge case has schedule new schedule: ", schedule.Moves)
            return schedule
        return None
    


    # STEP 1:
    # Easy Handover
    # IDEA: delay arrival clearing if: handover-estimation nur kurze Zeit bis ready
    #       and production interval relativ groß
    #       -> implement with else here maybe

    move = state.try_easy_handover()
    if move:
        schedule.Moves.append(move)
        # print("Easy handover new schedule: ", schedule.Moves)
        return schedule



    # INTERMEDIATE STEP:
    # collect information about the current state

    used_buffer_capacity = 0
    total_buffer_capacity = len(state.buffers) * state.buffers[0].max_height

    max_due = 0
    min_due = 999999999

    for buffer in state.buffers:
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
    # print("Buffer capacity", buffer_use,"%. Total:", total_buffer_capacity, "Used:", used_buffer_capacity)
    is_full_mode = state.is_full_mode(buffer_use)
    """True if buffer_use > MAX_BUFFER_USE else False
"""

    if is_full_mode:
        # find topmost ready blocks
        ready_blocks = state.get_topmost_ready_blocks()
        
        # if ready block found
        if ready_blocks:
            # print("Ready block exists")
            move = state.try_get_reachable_block(ready_blocks, total_buffer_capacity, used_buffer_capacity, max_due, min_due)
            if move:
                schedule.Moves.append(move)
                # print("Emergency reachable ready found new schedule: ", schedule.Moves)
                return schedule
        
        # try to clear accessible overdues
        if state.handover.is_ready:
            move = state.try_clear_accessible_overdues()
            if move:
                schedule.Moves.append(move)
                # print("Clear overdue new schedule: ", schedule.Moves)
                return schedule
                    
        # search for reachable overdue
        overdue_blocks = state.get_topmost_overdue_blocks()
        
        # if overdue block found
        if overdue_blocks:
            # print("Overdue block exists")
            move = state.try_get_reachable_block(overdue_blocks, total_buffer_capacity, used_buffer_capacity, max_due, min_due)
            if move:
                schedule.Moves.append(move)
                # print("Emergency reachable overdue found new schedule: ", schedule.Moves)
                return schedule

        # print("Cannot reach any of the blocks...")

        # IDEA: do clear arrival stack if nothing else to do AND incorporate mean arrival interval!

        return None



    # STEP 2:
    # Arrival Clearing

    # check capacity of arrival stack
    free_arrival_size = state.arrival.max_height - len(state.arrival.blocks)

    if free_arrival_size < state.arrival.max_height * ARRIVAL_UTILIZATION_LIMIT - K:
        state.clear_arrival(min_due, max_due, schedule)
        # print("Normal arrival clearing new schedule: ", schedule.Moves)
        return schedule
    


    # STEP 3:
    # Normal Buffer Shuffling
    
    if state.has_ready_blocks():
        # determine source & destination
        state.initialize_buffer_scores(max_due, min_due)

        # source: stack with best ready score
        source_stack = state.choose_source()

        # destination: handover if possible, else best deposition buffer
        destination_stack = state.choose_destination(source_stack)

        # create move if chosen destination is not full
        if destination_stack.deposition_score != 0:
            move = create_move(block_id=source_stack.top().id, source_id=source_stack.id, target_id=destination_stack.id)
            schedule.Moves.append(move)
            # print("Buffer shuffling new schedule: ", schedule.Moves)
            return schedule
    
    

    # STEP 4:
    # Buffer Sorting
    # print("TODO buffer sorting ...")

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

