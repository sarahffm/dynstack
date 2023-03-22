import csv
from typing import List
from typing import Any


def initialize_csv(file_name : str, kpis : List[str]):
    # Create a new CSV file
    with open(file_name, mode='w', newline='') as file:
        writer = csv.writer(file)
        
        # Write the header row
        writer.writerow(['Time', *kpis])


def addRow(file_name : str, values : List[Any]):
    # Add a new line to the CSV file
    with open(file_name, mode='a', newline='') as file:
        writer = csv.writer(file)
        
        # Write a new data row
        writer.writerow([*values])


def fill_gaps(file_name : str):
    # open the file
    # mode r+ for reading & writing
    with open(file_name, mode='r+', newline='') as file:
        # load data into list
        data = list(csv.reader(file))

        # search for empty values & replace them
        for row_i, row in enumerate(data):
            for val_i, val in enumerate(row):
                if val == '':
                    # initialize empty val if it is the first row
                    if row_i == 1:
                        data[row_i][val_i] = 0
                        continue
                    # insert the value of the previous row
                    data[row_i][val_i] = data[row_i-1][val_i]

        # write modified data to file
        with open(file_name, mode='w', newline='') as file:
            writer = csv.writer(file)
            for row in data:
                writer.writerow(row)
        
# fill_gaps('test.csv')

"""
initialize_csv('test.csv', ['BAT', 'TBT'])
addRow('test.csv', [0, '', 12])
addRow('test.csv', [1000, 15, 30])
addRow('test.csv', [2000, 15, 50])
addRow('test.csv', [3000, '', 45])
addRow('test.csv', [4000, 17, ''])
addRow('test.csv', [5000, 10, 30])"""
