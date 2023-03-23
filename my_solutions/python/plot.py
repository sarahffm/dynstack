import pandas as pd
import matplotlib.pyplot as plt

# Read CSV file
dataframe = pd.read_csv('./data/data.csv')

# Get x values from first column of CSV
x = dataframe.iloc[:, 0]

# Get y values from remaining columns of CSV
y = dataframe.iloc[:, 1:]

# Create plot
plt.figure()
for col in y.columns:
    plt.plot(x, y[col], label=col)

# Add labels and legend
plt.xlabel('Time (ms)')
plt.ylabel('KPI values')
plt.legend()

# save plot
name = './data/plots/figure.pdf'
plt.savefig(name, bbox_inches='tight')

# Show plot
plt.show()







"""
# generate some example data
x = [1, 2, 3, 4, 5]
y = [10, 0, 6, 4, 5]
# y2 = [2, 4, 6, 8, 10]

def plot_result(x : List[float], y : List[float]):

    # create the figure and the two axes
    fig, ax1 = plot.subplots()

    # plot the first dataset on the first axis
    ax1.plot(x, y, color='blue', label='y')
    ax1.set_xlabel('x')
    ax1.set_ylabel('y')
    ax1.tick_params(axis='y', labelcolor='black')

    # add a legend to the plot
    lines, labels = ax1.get_legend_handles_labels()
    #lines2, labels2 = ax2.get_legend_handles_labels()
    #ax2.legend(lines + lines2, labels + labels2, loc='upper right')

    # show the plot
    plot.show()


plot_result(x, y)
"""
