# Setting Up *With* a Fork with Visual Studio 

 * See also [Setting up a Repo in VS](SettingUpRepoInVS2015.md) for important background material.  
 * See also [Simple GitHub Setup and Workflow](SimpleGitWorkflow.md) for a simpler setup without a fork, 
   if you only need read-only access.  Note that this information is also relevant to this (more complex)
   workflow.   

 As mentioned, the first option is great if you only want read-only access or you have read-write access to the GitHub repo.
 If you want to be able to submit pull requests, you need to create a fork (which you will have read-write access to)
 and use that for your updates. To do this do the following:

   1. Open a web browser to the GitHub project page (in our case https://github.com/Microsoft/perfview)
   2. Make sure you have a GitHub account and that you are logged in (If you are logged into GitHub your username 
      in the very upper right corner of the display will be an icon for your user identity).  See https://github.com
    for information on getting an account. The instructions will tell you to download GIT tools.   You can
    do this if you like, however Visual Studio has all the GIT functionality you will need so you don't
    have to have them (but it does not hurt, and they are useful for more advanced scenarios).
   3. Click on the 'Fork' button in the upper right corner. If it asks you where to put the fork, you will wish
      to put it in your account area (same as your user name). Note that if the fork already exists it simply
    navigates to the existing fork, so this is also a way of finding a fork if needed. 
   4. Once that has completed (it does not take long), it will take you to a page that looks VERY similar to 
      the original but is in fact a clone. It should have a URL like https://github.com/vancem/perfview that has  
    your user name in it.  
   5. At this point you can follow the instructions above for [setting up a local clone](SimpleGitWorkflow.md).  The only difference is 
      that the GitHub URL will be the *URL of the fork and not the main repository*.   You may also wish to give
    it a local name that makes it clear that it is a fork (for example the local directory name for my fork is perfview-vancem).
    Otherwise keeping track of which were direct clones (option 1) and which were not (option2) becomes more difficult.  

### Creating a 'upstream' Repository alias.  

At this point you should have a local clone of your personal read-write fork of the main repository. However, this
is a FULLY INDEPENDENT clone which will NEVER SEE ANY UPDATES from the main repository unless you explicitly integrate 
(merge) them. To make it easy to update, it is good to create an alias for the original main repository.  Here
we show you how to do that in Visual Studio.  

  1. Click on the View -> Team Explorer menu item 
  2. Click on the top bolded header (may say 'Home' or 'Changes', ...) and right click on 'Settings'
  3. Click on the 'Repository Setting' hyperlink
  4. One of the sub-items in Repository setting page is 'Remotes' and under that is an 'Add' hyperlink. Click on that.
  5. This will bring up a dialog box where you are prompted for a Name and a 'Fetch' and 'Push' location.  
     There is also a checkbox (which should be checked) that says the Fetch and Push are the same. 
  6. Fill in the name as 'upstream' and fill the 'Fetch' textbox with the URL OF THE MAIN REPOSITORY (in our    
     example this is https://github.com/Microsoft/perfview. It should automatically fill in the 'Push' dialog.  
  7. Click the 'save' button. This new name should now show up in the 'remotes' list.  

What this does is name the original main repository 'upstream'. This makes it easier to refer to it in later commands
(like updating your fork with updates from the main repository).  You will see that your GitHub repo already has
a short name called 'origin' (this is very standard name to use for your 'cloud' repo that your local repo works with).
'upstream' is a traditional GIT name for the repository that a fork was created from.  

## Pulling updates from the Main (upstream) Repository into your Fork

Sooner or later, the main repo will have updates that you will want to pull into your fork. This
section tells you how to do this.  

If you go to your fork's GitHub web page (e.g. one that has your user name in it like https://github.com/vancem/perfview)
you will see that there is a line just before the description of files in the repo like
 
   * This branch is even with Microsoft:main. 

Or it may say

   * This branch is 4 commits behind Microsoft:main. 

You can see that this item is an indication of the main branch of your personal fork being 'in sync' with the main branch
of the main repo (which we have named upstream). From time to time you will want to pull in
all these changes to bring your fork up to date. We do this in three steps

  1. Fetching all changes from the upstream repository. This step makes all the changes (commits)
     in the fetched repository AVAILABLE for merging, but does not actually do any merging of branches.     
  2. Merge the changes from the upstream/main branch into the LOCAL repository's main branch.
  3. Push the changes from the LOCAL repository's main branch into the main branch of the GitHub fork (called origin). 

#### Step 1: Fetching All Commits for the Upstream Repository.  

  1. Click on the View -> Team Explorer menu item 
  2. Click on the bolded header (may say 'Home' or 'Changes', ...) and right click on 'Sync' option.  This will bring
     up the synchronization page, and one of the options will be 'Fetch'. Click on that. This brings up a dialog filled
   out with 'origin' which says it will pull all changes from the 'origin' repository (which is your GitHub fork).
   However we don't want that since we are only interested in the changes from the original main (which we named upstream).
   You should be able to type a down arrow and 'upstream' should be one of the choices (since we added 'upstream' as a
   known remote). Select 'upstream' and click the 'Fetch' button.  That will fetch all of the changes from 'upstream', 
   and put them in the LOCAL GitHub repository. So far we have made our local repository bigger (more commits) but we have 
   not changed any existing branch (the transitive closure of any branch is the same as it was before). 

#### Step 2: Merging the LOCAL repository's main branch to include changes from upstream/main

  1. Click on the View -> Team Explorer menu item 
  2. Click on the top bolded header (may say 'Home' or 'Changes', ...) and right click on 'Branches' item which show you
     all branches associated with the LOCAL repository (that is the repository clone on your local disk).   There will
   be at least one branch called 'main' as well as branches under 'remotes/origin' (these are branches in the
   private fork on GitHub) and 'remotes/upstream' (these are branches in the original main repository).   
  3. Make sure that the 'active branch' (the one in bold) is 'main'. If it is not, double click on main to make it
     the active branch.  If it fails (and it could easily fail because you have modified files), you will have to
   commit or undo those changes before proceeding. Ultimately you want 'main' to be the active branch.  
  4. Right Click on the 'main' branch, right click on it and select 'Merge from'.  Then in the 'merge from' textbox select
         upstream/main and click the 'Merge' button. This says we want to integrate (merge) all changes from the 
   upstream/main branch into the currently active branch (which is main).


In most cases this merge operation goes without a hitch and Visual Studio will auto-commit the merge.   However if there
is a merge conflict with changes that you have made to 'main' (you really should never do that, put changes in some other
branch (see below)), then you will need to resolve them and commit the merge explicitly.  

#### Step 3: Pushing the LOCAL repository to your GitHub Fork

  1. Click on the View -> Team Explorer menu item 
  2. Click on the bolded header (may say 'Home' or 'Changes', ...) and right click on 'Sync' option.  This will bring
     up the synchronization page, and one of the options will be 'Sync'. Click on that.   This will push all of your 
   changes to 'origin' (which is your GitHub fork) as well as pull down any changes from origin (there should be 
   none as you are the only one modifying this fork).  

At this point you should be able to go to your GitHub web page for the fork and see a line like this

  * This branch is even with Microsoft:main. 

Which says that you have integrated all the changes from the original main repository into your private fork.
Congratulations, you have successfully brought your private fork up to date with respect to its upstream main.   

## Creating a pull request to the Main Repository.  

Creating a pull request starts by making the modification in the local repository *in its own branch*.
Thus you have to follow the instructions in [Simple GitHub Setup and Workflow](SimpleGitWorkflow.md) for creating
a new branch, committing changes to the new branch, and pushing (syncing) those changes to your personal GitHub 
fork. Let's assume we did this using the branch name 'TestPullRequest' as the branch name.

One key aspect to syncing is that you must make sure that the new branch name is 'published' to the GitHub fork.
This publishing step can be done on the View -> Team Explorer -> Synchronization pane.   You should see at the
bottom a hyperlink that indicates that you wish to publish the branch 'TestPullRequest'  You will want to click
that hyperlink. This ensures that the name (and not just the (anonymous) commit) 'TestPullRequest' is copied into
the GitHub fork. Otherwise you will not find the new branch in the steps below.   

Once the branch exists in the fork, you can go to the GitHub web page for the fork (in our example this is
https://github.com/vancem/perfview)

Near the top on the left hand side of the web page, you will find a dropdown menu which selects the 'active'
branch. It is likely to be set to the 'main' branch. Use the dropdown menu to change it to the 'TestPullRequest'
branch. Then click on the 'New Pull Request' button that is beside it. 

At this point the page updates to a web page for the pull request. It prompts you to enter a description
of the pull request (but populates it with the commit message for the branch)). You will typically want to change
this to describe the pull request as a whole as well as a justification why the request should be accepted (its
advantages, etc). It will show you the delta associated with the pull request, which you can look over.  When
you are happy with it, you can complete the submission of the pull request.

At that point it is out of your hands. The pull request will send mail to the maintainers of the repository 
and they may comment on your change and may require changes. If you need to make changes, simply make the 
changes in the branch (in our case TestPullRequest), first locally, then sync to push it to the GitHub fork.
Doing so will automatically update the pull request.   Eventually the maintainers will either reject your request
(by closing it with an explanation), or accept it (in which case it will be merged into the main repository).
You can then use the 'Pulling updates' procedure above to pull changes into your GitHub fork, and from there
you can synchronize to bring it to your local repository.

Once that change is in the main repository, the branch (in our example TestPullRequest) is no longer needed.
You can go to the View -> Team Explorers -> Branches pane right click on it and select 'delete' to delete it.
The active branch must be on some other branch for the delete to succeed.  
