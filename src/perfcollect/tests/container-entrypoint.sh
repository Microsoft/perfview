#!/bin/bash

# Set the location of perfcollect.
perfcollect=/src/perfcollect

# Enable debug logging.
set -x

# Test installation.
$perfcollect install

# Capture a short trace.
$perfcollect collect test-trace -collectsec 5

# Open the trace.
unzip test-trace.trace.zip

# Test the size of the files in the trace.
ls test-trace.trace/*
perf_data_size=`wc -c < test-trace.trace/perf.data`
perf_data_txt_size=`wc -c < test-trace.trace/perf.data.txt`
if [ "$perf_data_size" == "0" ]
then
    exit -1
fi
if [ "$perf_data_txt_size" == "0" ]
then
    exit -1
fi

### Test capturing CLR events

# install sdk
mkdir sdk
cd sdk
curl -OL https://dot.net/v1/dotnet-install.sh
bash ./dotnet-install.sh --install-dir .

# run ConsoleApp1 in background
cd /src/tests/ConsoleApp1
/src/sdk/dotnet run &

appPid=$!

# Capture a short trace with dotnet-trace activated.
$perfcollect collect test-dotnettrace -pid $appPid -dotnet-trace -collectsec 5

# kill ConsoleApp1
kill -9 $appPid

# Open the trace
unzip test-dotnettrace.trace.zip

# Test the size of the trace.nettrace file
if [[ ! -s test-dotnettrace.trace/trace.nettrace ]]
then
    exit -1
fi

