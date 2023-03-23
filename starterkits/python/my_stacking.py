import zmq
import sys

import solver_rule
import CsvEditor

# import hotstorage;
# import rollingmill;


if __name__ == "__main__":
    # check if right number of arguments
    if len(sys.argv) < 3:
        print("""USAGE:
        python dynstack ADDR ID PROBLEM""")
        exit(1)
    if len(sys.argv) == 4:
        print("My constum stacking will be started.")
        [_, addr, id, problem] = sys.argv
        is_rollingmill = problem=="RM"
    
    # connect to simulation
    context = zmq.Context()
    socket = context.socket(zmq.DEALER)
    socket.setsockopt_string(zmq.IDENTITY, id)
    socket.connect(addr)
    print("Connected socket")

    # prepare KPIs
    # TODO get info from message
    HS_KPIs = ['BlockedArrivalTime', 'BufferUtilizationMean', 'CraneManipulations', 'CraneUtilizationMean', 'DeliveredBlocks', 'HandoverUtilizationMean', 'LeadTimeMean', 'ServiceLevelMean', 'TardinessMean', 'TotalBlocksOnTime', 'UpstreamUtilizationMean']

    # create a new data .csv file to track KPIs
    print("Creating csv ...")
    file_name = 'data.csv'
    CsvEditor.initialize_csv(file_name, HS_KPIs)

    # listen to simulation's messages
    while True:
        msg = socket.recv_multipart()
        print("recv")
        plan = None

        # for now just hotstorage
        if not is_rollingmill:
            plan = solver_rule.plan_moves(msg[2], file_name)

        if plan:
            print("send")
            msg = plan.SerializeToString()
            socket.send_multipart([b"", b"crane", msg])
        else:
            socket.send_multipart([b"", b"crane", b""])
            

